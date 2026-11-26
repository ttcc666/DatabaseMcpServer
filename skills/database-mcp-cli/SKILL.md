---
name: database-mcp-cli
description: "使用 DatabaseMcpServer CLI（tool、export、config、init、-web）管理连接、执行 SQL 或 MongoDB 查询与写入、编写调用脚本及排查错误。适用于通过命令行操作数据库，不用于开发服务器源码或配置 MCP stdio 客户端。"
---

# DatabaseMcpServer CLI

帮助调用者选择工具、构造参数、执行数据库操作并判断结果。依据实际可执行文件的帮助，保留用户指定的连接、权限和操作范围。

## 入口与版本

- 命令入口：`DatabaseMcpServer`；优先 `--version` 查看实际执行程序版本。旧安装若不支持，再用 `dotnet tool list --global` 或 `--tool-path` 核对。
- 工具名称和参数以实际运行的 `tool list --json` / `tool help <name> --json` 为准。不同安装版本可能提供不同工具，不根据文档中的固定数量判断能力。

## 调用流程

1. 用 `tool list`、`tool help <name>`、`config help <subcommand>` 确认当前名称和参数。
2. 确定可执行文件与配置文件。脚本优先显式 `--config`；执行工具时优先使用支持的 `--database-name`，它不改变当前选择。
3. 查询 `list_databases` / `get_current_database`；离线能力用 `get_database_capabilities`。只有需要连通性证据时才执行 `test_connection --database-name` 或 `config test`。
   使用 `capabilityDetails` 选择 schemaSections、diagnostics kind 和 cliExport 格式；provider-dependent/unverified 需要按目标数据库验证。目录取连接并集，列出工具不保证每个目标都支持；JSON help 的 unsupportedProviders 指明已知排除项。
4. SQL 的表结构未知时先用 `get_table_schema`，普通查询用 `query_database`，按表分页用 `query_table_page`；Mongo 在已知集合上用 `mongo_find` / `mongo_aggregate`，没有集合枚举工具，不发送 SQL。已有可靠的目标和结构信息时直接复用。
5. 已获用户授权的写入按帮助附加 `--yes`，同时遵守服务端及连接权限。
6. 同时检查退出码、JSON、逐项结果、截断标志和写入确认状态。报告实际执行结果；日志中的连接串、密码和敏感参数需脱敏。

## 当前契约

- 发现 SQL 对象优先 `list_objects`；结构按需选择 `get_table_schema --sections`，定义用 `get_object_definition`，估算计划用 `explain_query`。原生 SQL 对象元数据/计划支持 SQLite、MySQL、SQL Server、PostgreSQL；其他 SQL provider 的 list_objects 使用 SqlSugar 目录并标记 pagingMode=client；SQL Server explain 仅应用标量参数类型，parameterMode=types-only。full/analysis 在 MCP、CLI、Web 均按配置 provider/权限并集过滤；`tool list --all --json` 仅用于静态契约查看，不放宽调用权限。

- 当前已删除重复的连接测试、细粒度结构列表和 single/scalar 入口，不保留别名。单行/标量使用 `query_database`（首行 `maxRows=1`，聚合列显式命名）；检查 `data` 空数组及截断标记。对象列表按 kind 调用 `list_objects`，结构按 sections 调用 `get_table_schema`。
- SQL 参数统一为 `[{name,value,dbType?,direction?,size?,precision?,scale?}]`。NULL 必须指定 dbType；IN 数组通过 value 传递且不能声明 dbType；输出方向只用于存储过程，字符串/二进制输出需要 size。
- `batch_sql_query` 仅接受 commands 对象数组 `[{sql,parameters?}]`，1–5 项，不兼容 queries；`batch_execute_commands` 接受 `[{sql,parameters?}]`，默认非事务。显式 `--atomic true` 在 SQLite/SQL Server/PostgreSQL 支持整批 DML，检查 transactionOutcome；unknown 不自动重放。
- 非原子批量工具的 `commandTimeoutSeconds` 覆盖排队和整批执行；atomic 模式由 `totalTimeoutSeconds` 控制整批截止时间、`commandTimeoutSeconds` 限制单条命令。清理可能超过 deadline。逐项检查 `results[]`，顶层成功和退出 0 不代表全部成功。
- 使用当前工具说明：旧 `sql_query`、`sql_query_with_in`、`get_table_data` 已移除；没有字典式 SQL 参数、`--parameters-array` 或分离的存储过程输入/输出参数入口。
- `query_database` 默认 1000 行、1 MiB、30 秒，字节预算至少 1024；`query_table_page` 默认 100 行且要求非空主键。不传 columns/filters/orderBy 时将 nextCursor 原样传回 cursor，不与 page>1 混用；传任一新选项（含空 filters/orderBy）改用 offset 页码、不返回 cursor，整页物化后检查字节预算。选列最多 64、AND 筛选最多 32、排序最多 8 项，字段区分大小写；支持 eq/ne/gt/gte/lt/lte/isNull/isNotNull 和 asc/desc，排序追加缺失主键。服务端上限仍生效，达到行上限不证明有下一页或已经读完。
- `insert_row --table-name ... --row '{...}' --yes` 只用于 SQL Server/MySQL/SQLite 的普通表名和唯一整数自增列；SQLite 需要 AUTOINCREMENT。row 为 1–128 字段、最多 4 MiB，值仅标量/null，不能指定自增列。返回字符串 identity 和 identityColumn；PostgreSQL、schema 限定名或复杂类型使用参数化 execute_command。取消不保证回滚，不自动重试。
- `DB_TOOL_ACCESS` 与连接 `accessMode` 取更严格权限。read-only 禁止写；read-write 禁止 DDL、schema 变更和存储过程执行。CLI 的 `--yes` 不覆盖权限或 `enableDangerousOperations`。
- Mongo 当前提供 mongo_find、mongo_aggregate、mongo_insert_many、mongo_update、mongo_delete、mongo_truncate 六个工具。单条插入也用 documents 对象数组，返回 insertedCount，不返回 insertedId。update/delete 始终批量执行、要求危险操作开关，空 filter 还需 allowAll；many/upsert 已移除。truncate 要求 schema-admin 和危险操作开关。集合枚举、独立 count、集合元数据和 explain 不开放；计数可使用只读 $count pipeline。
- Mongo 通过 SqlSugar 内置 ADO，URI 必须含业务库且拒绝 mongodb+srv。输入接受 Extended JSON v2，输出为 provider 转换后的 JSON，不保证 BSON 无损往返；ObjectId 输出字符串，作为 ObjectId 条件时须重新用 $oid 包装。先物化再检查字节预算，初始同步查询可能延迟取消；writeOutcome=unknown 或取消后先核实实际状态，不自动重放。

- `get_table_schema` 支持 tableName 或 tableNames 二选一，最多 50 表；sections 按需读取，行数/字节预算全批共享（含缓存命中），逐表检查 status。SQLite/MySQL/PostgreSQL/SQL Server/DuckDB 支持异步 schema。
- DDL 仅用 `columns` / `column` 自有字段：name/dataType/length/precision/scale/nullable/primaryKey/identity/comment/defaultExpression；不接受旧 SqlSugar 字段。create_index/add_primary_key 使用 columnName 或 columnNames 二选一，数组 1–32 列；drop_index 使用 tableName/indexName，均受 provider 能力和 schema 权限约束。
- Mongo lookup/facet 限当前数据库、全树 64 stage、4 层、每 facet 8 分支，facet 不可嵌套；仍拒绝写 stage 和 JavaScript。
- 大量导出用顶级 `export --sql ... --format csv|jsonl --output ...`，支持 typed parameters，默认 100000 行/256 MiB/30 秒，受服务端上限约束；已有文件需 --overwrite。摘要 limitReached=true 不能证明完整导出。CSV 原值保留公式前缀，NULL 为空；JSONL 保留类型。
- 元数据、表分页、存储过程、诊断和导出共享包含排队的总 deadline。存储过程达到预算返回 1013，输出参数不可用且副作用不回滚。错误检查 errorCode/retryable/phase/capability/hint；写入失败不得自动重放。

## 配置与状态

| 对象 | 行为 |
| --- | --- |
| `config use` / `config set-default` | 修改配置文件 isDefault |
| `tool switch_database` | 按配置路径持久化 CLI 当前选择到用户目录的 cli-state.json |
| 工具的 `--database-name` | 只指定本次请求目标；错误名称不会回退到当前库 |
| `reload_database_config` | 当前名字仍存在时保留选择 |
| 编辑响应 | 分别检查 saved、runtimeApplied、selectionPersisted，不能把保存成功当成全部成功 |

`tool` / `-web` 查找顺序：`--config → ./databases.json → ./local-databases.json → DB_CONFIG_PATH → 用户目录/.database-mcp/databases.json`。`init` / `config` 默认写用户目录，需用 `--config` 指定其他路径。测试使用临时配置和临时对象。

## 输出与取消

异步 SQL 的 `commandTimeoutSeconds` 包含排队、执行与读取，默认服务端查询超时（通常 30 秒）；非原子批次共享总预算。原子批次仍使用 `totalTimeoutSeconds` 控制总 deadline，`commandTimeoutSeconds` 为单条限制。同步 GO/DDL 使用驱动超时。`0` 必须符合服务端无限等待策略。队列限额/超时为 1009，总 deadline 为 1011，取消不保证写入回滚。

`tool list --json --all` 返回 `capabilityCatalog`，包括表结构 section 和产品能力。`get_database_capabilities` 离线报告指定库的能力，`test_connection` 验证连通性。

| 退出码 / 输出 | 处理 |
| --- | --- |
| 0 | 调用成功；仍检查批量子项和结果边界 |
| 1 | 工具或配置操作返回失败；检查 errorCode、部分保存或写入状态 |
| 2 | 用法、参数绑定、配置路径或启动失败；先读 stderr |
| 130 | 操作取消；不代表写入已回滚 |

普通帮助输出到 stderr；执行结果及 `tool list/help --json` 输出到 stdout。`get_database_config` 没有顶层 success，不能据此判失败。

## 按需阅读

| 任务 | 参考 |
| --- | --- |
| 配置定位、选库、监听、状态与权限 | [CLI 工作流](references/cli.md) |
| SQL 参数、批量写、GO、Mongo 和 PowerShell 示例 | [命令示例](references/commands.md) |
| 退出码、旧参数、超时、部分成功、驱动与 TLS | [故障排查](references/troubleshooting.md) |

这些参考随技能独立分发，无需项目源码即可使用；完整工具目录以实际 `tool list/help` 为准。
