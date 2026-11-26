# DatabaseMcpServer 4.0.0

> 本文记录已发布 4.0.0 的历史契约，工具数量、Mongo API 和源码框架描述均对应当时版本。当前源码的破坏性变更见[5.0.0 说明](release-5.0.0.md)，不要将本页旧示例直接套用到新版本。

本版本升级数据库工具契约，新增 MongoDB 原生集合查询与写入，并将后端拆分为 Host、Core、Infrastructure 三层。总计 62 个工具；发布包要求 .NET 10 / ASP.NET Core 运行时，源码和测试保留 .NET 9 / 10。

从已发布的 4.0.0 升级到后续版本时，参见[当前工具迁移说明](tool-evolution.md#旧入口如何收敛)。

## 从 3.7.0 升级

这是破坏性升级。更新安装包并重启 MCP 客户端后，重新获取 `tools/list`；CLI 使用 `tool list` 和 `tool help <name>`。配置兼容行为保留，旧工具入口不再转发。

| 旧调用 | 4.0.0 调用 |
| --- | --- |
| `sql_query` | `query_database` |
| `sql_query_with_in_parameter` | `query_database`，参数 `value` 传 JSON 数组，IN 数组不声明 `dbType` |
| `call_stored_procedure_with_output` | `call_stored_procedure`，用参数的 `direction`、`dbType`、`size` 声明输出 |
| SQL 参数字典 | `[{"name":"id","value":1,"dbType":"Int32"}]`；NULL 必须指定 `dbType` |
| 批量 SQL 字符串与平行参数数组 | `commands=[{"sql":"...","parameters":[...]}]`；逐项检查结果，批量写不是事务 |
| .NET 9 工具运行环境 | 安装 .NET 10 SDK，或所需 .NET / ASP.NET Core 运行时 |

```powershell
dotnet tool update --global 'DatabaseMcpServer' --version '4.0.0'
dotnet tool list --global
DatabaseMcpServer tool list
DatabaseMcpServer tool help query_database
```

## 工具与数据库能力

- SQL 查询支持单次命名连接、类型化参数、行数/字节预算和结构化结果；`query_table_page` 按主键分页，不隐式 COUNT；`get_database_capabilities` 离线报告实际 provider、驱动和操作能力。
- MongoDB 新增 `mongo_list_collections`、`mongo_find`、`mongo_count`、`mongo_insert_one`、`mongo_update`、`mongo_delete`，使用 JSON / Extended JSON v2。`writeOutcome=unknown` 或取消后应核实实际数据，不能假定回滚后直接重试。
- 补齐 HG、GBase、TDengine、DuckDB 扩展与托管驱动。GBase、TDengine 仍有部署端原生客户端要求；DB2 / HANA 不支持。驱动随包提供不代表所有产品和平台都已完成实库验证。
- 更新 MCP SDK 至 2.4.0、SqlSugarCore 至 5.1.4.221、MongoDB.Driver 至 3.11.1，并同步兼容依赖。

## 执行与维护

- SQL 使用独占客户端租约和驱动物理连接池；Mongo 使用原生客户端。统一连接快照、执行准入、权限和超时策略。
- 配置响应区分 `saved`、`runtimeApplied`、`selectionPersisted`，避免部分成功时重复执行已保存操作。
- 修复只读 PRAGMA 写入、数值精度和 MCP 错误标志；CLI 取消返回 130，写入取消不保证回滚。
- 生产代码与测试按功能模块组织；CLI skill 专注工具调用、参数、结果和排错。发布验证检查程序集、原生资源、工具清单和 NuGet 包大小。

SQL 批量超时逐条生效，查询预算整批共享。服务端与连接权限取更严格值，CLI `--yes` 不能绕过权限。Mongo 多文档和空过滤写另有危险操作保护。

完整说明：[工具清单](../TOOLS.md)、[CLI](cli.md)、[Mongo 集合契约](mongodb-collections.md)、[驱动要求](provider-drivers.md)、[架构边界](backend-architecture.md)。
