# MCP / SqlSugar 依赖升级与兼容性

日期：2026-11-20。落实 `plans/2026-11-20-mcp-next-stage.md` 中的关键缺陷修复和依赖升级；未使用子代理，未发布 NuGet 包。

## 升级范围

| 组件 | 原版本 | 本次版本 | 原因 |
| --- | --- | --- | --- |
| ModelContextProtocol / Core | 2.0.0 | 2.4.0 | 官方稳定版，包含 stream/stdio 客户端取消通知修复 |
| SqlSugarCore | 5.1.4.216 | 5.1.4.221 | 官方稳定版；以真实驱动回归验证兼容性，不推断性能收益 |
| SqlSugar.MongoDbCore | 5.1.4.277 | 5.1.4.280 | 同步 provider；带入 MongoDB.Driver 3.11.1 |
| Microsoft.Extensions.Hosting / FileProviders.Embedded | 10.0.10 | 10.0.15 | 满足 MCP 的 10.0.14 依赖下限，保留 .NET 10 版本线 |
| SharpCompress | 0.48.0 | 0.48.1 | 满足 MongoDB.Driver 3.11.1 依赖下限，消除 NU1605 |
| Snappier | 1.3.1 | 1.3.2 | 同版本线补丁更新 |
| Microsoft.NET.Test.Sdk | 18.8.1 | 18.12.0 | 更新测试基础设施，保持 xUnit 2 / VSTest |
| SDK / global.json | 10.0.201 | 10.0.404 | 固定到本机已验证的 SDK，保留 latestFeature 策略 |

OceanBase、ClickHouse、GaussDB provider 已核对，没有更新的稳定版。MySqlConnector 2.6.2、Pkcs 8.0.2 和 SQLite native 3.53.3 的安全固定继续生效；未引入 MySqlConnector 3、SharpCompress 1 或 .NET 11 等额外大版本迁移。SqlSugarCore 本次带入 Microsoft.Data.Sqlite 10.0.11；实际解析的 SQL Server / PostgreSQL 驱动为 Microsoft.Data.SqlClient 5.2.2 / Npgsql 5.0.18。

## 行为修复

### 只读查询

`SqlSafetyGuard` 对 PRAGMA 使用明确允许列表：`user_version` 等配置仅允许读取；`table_info('name')`、`index_list`、`foreign_key_list`、`integrity_check` 等元数据/检查指令允许其只读参数形式。拒绝赋值、`wal_checkpoint`、`optimize`、未知 PRAGMA，以及 `EXPLAIN PRAGMA`；SQLite 官方说明部分 PRAGMA 在 prepare 阶段执行，EXPLAIN 不保证无副作用。

同时拦截 `SELECT INTO` / `INTO OUTFILE` 和 MySQL/MariaDB 可执行注释。普通注释、引号内分号和标识符内引号不会掩盖这些检查。SQL 原文仍交给 SqlSugar 执行，检查过程不改写实际 SQL。

这是已知 SQL 副作用的防护，不是覆盖所有方言/用户函数的 SQL 解析器。调用可写函数等情况仍需数据库账号权限约束；`readOnlyHint` 是 MCP 注解，`enableDangerousOperations` 也不是全局只读角色。本次没有改变 command/schema 工具的权限语义。

### JSON 数字

统一参数转换按 Int64 → 精确 decimal → 可往返 double 选择类型。`TryGetDecimal` 返回成功后仍使用 `JsonElement.DeepEquals` 校验数值，避免 `1e-50` 变为 0 或高精度小数被舍入。

- 保留 9007199254740993、大整数边界、decimal 金额和数组转换。
- `1e-50`、`1e100` 使用有限 double；double 仍遵循 IEEE 754，往返校验指 JSON 十进制数值的序列化一致性，不承诺任意十进制数的精确二进制表示。
- `1e-400`、`1e400` 和不能往返的高精度数字返回 `InvalidParameters`，不静默改值。如需数据库支持的更高精度，传字符串并在 SQL 中显式 CAST。

### MCP 错误和取消

生产 `AddDatabaseMcpServer` 注册官方 `WithRequestFilters` / `AddCallToolFilter`。工具顶层 `success=false` 时设置 `CallToolResult.IsError=true`；此后已移除非配置兼容接口，当前工具名称和参数 schema 见[兼容清理记录](compatibility-cleanup.md)。状态解析跳过 data，不为整个查询结果额外构造 JSON DOM。驱动 `DbException` 归类为 `QueryExecutionFailed`。

批量请求仍按原约定在顶层表示已处理，并在逐项结果报告失败；不把部分成功变为整体 MCP 错误，避免调用方错误地重试已完成操作。取消异常继续透传。

协议测试现在使用生产 DI 注册，分别覆盖默认协议与 `2025-11-25`，只取消客户端 token，验证服务端收到取消、等待任务退出、后续查询可用。外部 MCP 客户端自己的取消实现需要其对应版本支持，升级服务端不会升级外部客户端。

## 验证

- 常规测试：net9.0 / net10.0 各 **314 passed、0 failed、0 skipped**；集成测试通过独立脚本运行，不计入此数。
- 三库 Docker：每个目标框架 **25 passed、0 failed、0 skipped**，包括新增极小数普通/有界查询；本次容器和匿名卷已清理。镜像 digest 与 [provider 基线](provider-integration.md) 一致。
- Release solution build：**0 warnings、0 errors**。
- NuGet audit：生产、测试项目含传递依赖均未报告已知漏洞。
- 打包检查：以命令行临时版本 `3.7.1-upgrade.20261120` 生成本地验证包，安装到 Git metadata 下独立 tool-path；CLI 和真实 stdio 启动通过，发现 56 个工具，确认只读状态不变、极小数保留、非法数值/数据库错误的 isError。项目正式 PackageVersion 仍为 3.7.0，未发布。
- 故障注入：将 decimal 精确性判断中的 `&&` 临时改为 `||`，15 项数字测试中 6 项失败；恢复源码并重建后 15 项全部通过。中间报告保存在 Git metadata 下，不进入交付文件。

| Requirement | Evidence |
| --- | --- |
| 拒绝只读副作用且数据库状态不变 | `EnsureReadOnlySql_RejectsSideEffects`、`ReadOnlyPragma_RejectsWritesWithoutChangingDatabase` |
| 保留合法元数据读取 | `EnsureReadOnlySql_AllowsMetadataAndReadOnlyStatements` |
| 极小数和 decimal 边界 | `ParseParameters_PreservesFloatingPointRangeWithoutDecimalUnderflow`、`ParseParameters_PreservesExactDecimalBoundaries`、`SqlQuery_PreservesTinyNumbersThroughSqlite` |
| 拒绝数值静默舍入/溢出 | `ParseParameters_RejectsNumbersThatWouldChangeDuringConversion` |
| MCP 错误、部分成功、当前 schema 与客户端取消传播 | `Tools_PublishCurrentSchemasAndQueryPayloadsOverProtocol`（默认协议和 2025-11-25） |
| 真实驱动升级回归 | `TinyNumbers_RoundTripWithoutBecomingZero`、`ParametersAndTypedRows_RoundTripThroughTools`、`InFlightCancellation_StopsServerWorkAndReleasesSlot` 及既有 provider suite |
| 安装后的真实 CLI/stdio | `.git/testagent/upgrade-20261120/probe.py`：CLI 失败退出码 1、56 个工具、协议 2025-11-25、PRAGMA 状态 0、1e-50、错误标志断言通过 |

在仓库根目录复现：

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'Category!=Integration'
pwsh -NoProfile -File 'scripts/test-integration.ps1'
dotnet list 'DatabaseMcpServer.slnx' package --vulnerable --include-transitive --no-restore --format 'json'
dotnet build 'DatabaseMcpServer.slnx' -c 'Release' --no-restore
dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c 'Release' '-p:TargetFrameworks=net10.0' '-p:BuildWebsite=true' --artifacts-path 'artifacts/package-build'
```

保留 net9.0/net10.0 双目标兼容；.NET 9 已于 2026-11-10 结束支持，新部署推荐 .NET 10 LTS。MongoDB 和其他附加 provider 本轮完成依赖/编译检查，未宣称完成真实库 CRUD 回归。

类型化 MCP 参数/结果、默认查询预算、按请求指定数据库、schema 缓存及 provider 能力表仍在后续计划中。当前不新增 outputSchema/structuredContent，也不宣称吞吐提升百分比。

## 官方依据

- [MCP 2.4.0 release](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0) 与 [取消修复 #1917](https://github.com/modelcontextprotocol/csharp-sdk/pull/1917)。
- [MCP 2.4.0 请求过滤器](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.4.0/docs/concepts/filters.md)、[Tools 规范的错误语义](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)。
- [SqlSugarCore 5.1.4.221](https://www.nuget.org/packages/SqlSugarCore/5.1.4.221)、[Mongo provider 5.1.4.280](https://www.nuget.org/packages/SqlSugar.MongoDbCore/5.1.4.280)。
- [SqlSugar 生命周期](https://www.donet5.com/Home/Doc?typeId=1224)、[ADO/参数/reader](https://www.donet5.com/Home/Doc?typeId=1198)：继续使用独立 SqlSugarClient、IsAutoCloseConnection、参数绑定及 reader 释放。
- [SQLite PRAGMA](https://www.sqlite.org/pragma.html)、[JsonElement.DeepEquals](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0)。
- [.NET 发布与支持元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json)。
