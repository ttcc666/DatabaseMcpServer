# 非配置兼容层清理

2026-11-20，检查范围为本地未推送的“优化1”至“优化6”（基线 `5452fe7`）及工作区。本轮直接采用当前工具契约，不为旧调用增加别名或包装。配置文件无需迁移。agent 升级后重新读取 `tools/list`，CLI 使用 `tool list` / `tool help`。

## 当前接口

工具数从 59 减至 56。主要变化如下：

| 删除的入口或格式 | 当前调用方式 |
| --- | --- |
| `sql_query`、`sql_query_with_in_parameter` | `query_database`；IN 值放入 `parameters[].value` 数组 |
| `call_stored_procedure_with_output` | `call_stored_procedure`；通过 `parameters[].direction` 指定输入/输出 |
| JSON 字符串参数、`typedParameters` 双轨、名称式 `outputParameters` | 单一 `parameters` 类型化数组；NULL/输出要求 dbType，字符串/二进制输出要求 size |
| 批命令的 SQL 字符串、字符串数组、二次编码数组、`parametersArray` | `commands: [{sql, parameters?}]` |
| 批查询的无约束 JsonElement schema | `queries: string[]`，1–5 条只读 SQL |

未知参数和不符合当前 schema 的参数会被拒绝，不静默忽略。参数精度、权限、命名连接、取消、部分成功与预算语义继续生效。

```json
{
  "sql": "select * from orders where id in (@ids)",
  "databaseName": "reporting",
  "parameters": [{ "name": "ids", "value": [1, 2, 3] }],
  "maxRows": 100
}
```

## 内部实现

- 删除共享 `SqlSugarScope` / `CreateClient*`、缓存退役列表、默认接口兜底、`IsExclusive` / `CopyNew` 分支及空清理回调。
- 工厂只维护准入身份；每次租约新建并释放独立 SqlSugarClient，物理连接由驱动池复用。首次配置加载即登记身份，重载与重命名继续遵守既有准入语义。
- 执行策略、元数据缓存和连接身份改为必需依赖，不在工具内部补建缺失依赖。
- 多结果和 batch 统一 reader 路径。删除旧完整 DataSet 路径和模拟旧 SqliteDataAdapter 的 DataTable 类型转换；结果值按 reader 实际类型输出，重复列名生成唯一后缀。SQLite 的 NULL 首行不再把后续整数转成二进制；datetime 文本按原值返回，未额外转换为 DateTime。

保留的配置行为包括 `allowDangerousOperations` 字段/CLI 别名、配置发现与状态迁移、Web 配置重命名入口，以及 `DB_QUERY_BOUNDED_DEFAULTS` 等执行策略配置。该开关仍控制 batch/多结果等工具省略预算时的默认值；不再切换两套执行实现。`query_database` 始终填充预算。数据库驱动差异与当前 net9/net10 目标保留。

## 验证

在仓库根目录运行：

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'Category!=Integration'
pwsh -NoProfile -File 'scripts/test-integration.ps1'
pwsh -NoProfile -File 'scripts/test-package.ps1'
pwsh -NoProfile -File 'scripts/verify-cli-tools.ps1' -Framework 'net10.0' -Configuration 'Release'
```

| 要求 | 具体测试 |
| --- | --- |
| 当前 MCP schema、移除旧入口、拒绝旧参数 | `Tools_PublishCurrentSchemasAndQueryPayloadsOverProtocol` |
| CLI/Web 拒绝旧格式并执行当前命令 | `BindArguments_RejectsObsoleteShapes`、`WebTools_RejectObsoleteArgumentsAndExecuteTypedBatch` |
| 独占租约、重载身份和及时释放 | `UpdateConnections_PreservesIdentityAndCapturesNewConfiguration`、`RemovalAndReaddition_ReleasesIdentityAfterLastLease`、`ClientLeaseTests`、`ExecutionConcurrencyTests` |
| 原始 reader 值、NULL 首行、重复别名 | `BoundedMultiple_PreservesReaderValuesAndDuplicateColumnOrdinals`、`MultipleResults_KeepsMixedTypesAndResolvesAliasCollisions` |
| 类型化参数、输出、取消及预算 | `TypedParameterTests`、`JsonParameterTests`、`QueryToolsTests`、`CommandCancellationTests` 和三库 `ProviderIntegrationTests` / `DatabaseRoleIntegrationTests` |
| 配置兼容与更新 | `DatabaseConnectionTests`、`DatabaseConfigServiceTests`、`CurrentDatabaseStateStoreTests`、`ConfigurationEditingTests` |

2026-11-20 实际运行结果：

| 验证范围 | 结果 |
| --- | --- |
| 常规测试，net9.0 / net10.0 | 每个框架 357 passed、0 failed、0 skipped |
| MySQL 8.4 / PostgreSQL 17 / SQL Server 2022 隔离容器集成，net9.0 / net10.0 | 每个框架 38 passed、0 failed、0 skipped；容器和匿名卷已清理 |
| 最后一处 CLI 参数绑定清理后的定向回归，net9.0 / net10.0 | 每个框架 49 passed、0 failed、0 skipped，覆盖 CLI 绑定/目录、Web 权限与查询取消 |
| 临时安装 NuGet 工具包并运行真实 stdio / CLI | 56 tools、协议 2025-11-25；失败退出码、只读保护、极小数精度、错误标志和 structuredContent 一致性均通过 |
| SQLite CLI 清单 | 覆盖 56/56；43 个成功、13 个不支持操作；无额外失败 |

CLI 报告保存在 `artifacts/cli-tool-verify/20261120-031535/`（本机验证产物）。13 个失败包含 SQLite 不支持的约束/注释/数据库列表、函数和存储过程操作，并非声明全部工具在 SQLite 可用。

故障注入：临时反转 MCP 未知参数检查条件，两个协议测试均因接受旧 execute_command 参数而失败；随后恢复源文件，常规测试全部通过。

最后的定向回归命令：

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'FullyQualifiedName~CliCommandParserTests|FullyQualifiedName~CliToolCatalogTests|FullyQualifiedName~DatabaseRoleTests|FullyQualifiedName~QueryCancellationTests'
```

实现依据：[MCP C# SDK 2.4.0 tools](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.4.0/docs/concepts/tools/tools.md)、[SqlSugar ADO](https://www.donet5.com/Home/Doc?typeId=1198)、[SqlSugar 生命周期](https://www.donet5.com/Home/Doc?typeId=1224)。
