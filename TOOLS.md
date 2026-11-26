# DatabaseMcpServer Tools 使用手册

静态目录包含 **54 个** MCP 工具。本文描述[5.0.0](Doc/release-5.0.0.md)的当前源码契约，安装版以 `tool list/help --json` 为准。MCP、CLI 和 Web 的 full 与 analysis 按配置连接的数据库类型和权限筛选。`tool list --json --all` 查看完整静态契约。迁移与限制见[工具扩展](Doc/tool-evolution.md)，安装、配置和退出码见 [CLI](Doc/cli.md)。

## 调用约定

`tool list --json --all` 的 `capabilityCatalog` 导出表结构 section 和产品能力声明，详见[能力参考](Doc/capabilities.generated.md)。构建后运行 `scripts/update-tool-manifest.ps1` 同步清单和参考。

- MCP tool 名统一使用 `snake_case`；MCP 参数使用源码中的 `camelCase`。
- Query、Command、Schema 工具均支持可选 `databaseName / --database-name`，推荐每次显式选库，不修改 currentDatabase。
- CLI 调用格式：`DatabaseMcpServer tool <tool_name> [options]`，参数使用 `--kebab-case`。
- 下表的参数格式为 `MCP 参数 / CLI 参数`；方括号表示可选参数。
- CLI 示例省略 `--config` 时会按 `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json` 查找配置。自动化场景建议显式追加 `--config 'D:\config\databases.json'`。
- 标记为“是”的工具会修改数据或 schema，CLI 必须追加 `--yes`。
- PowerShell 中 SQL 和 JSON 参数应使用单引号包裹。调用前可运行 `DatabaseMcpServer tool help <tool_name>` 查看实时参数。
- 异步 SQL 工具的 `commandTimeoutSeconds / --command-timeout-seconds` 是包含排队、执行、读取的总 deadline，默认服务端查询超时（通常 30 秒），`0` 表示无限等待且受服务端策略约束，合法范围 `0–86400`。非原子批次共享一次总预算；原子批次仍由 `totalTimeoutSeconds` 控制事务总 deadline，`commandTimeoutSeconds` 限制每条命令。同步 GO/DDL 保留驱动命令超时语义。

## 执行上限与取消

MCP、CLI、Web 共用执行准入，SQL 使用独占客户端租约。默认每连接执行 **8**、排队 **32**，应用协调器总执行 **32**、总排队 **128**，排队最多 **30 秒**。两层额度联合分配；队满/排队超时返回 `errorCode=1009`（DatabaseBusy）。启动前可用 `DB_MAX_CONCURRENT` / `DB_MAX_TOTAL_CONCURRENT`（1–1024）、`DB_MAX_QUEUED` / `DB_MAX_TOTAL_QUEUED`（0–10000）、`DB_QUEUE_TIMEOUT_SECONDS`（1–86400）调整，非法配置拒绝启动。独立进程/Host 分别计数。

请求在排队前固定数据库和权限快照；切库、重载只影响之后接纳的请求。重载和重命名继续共享原并发额度；独立配置项即使指向同一物理数据库也分别计算。当前数据库是服务共享的选择，并非每个 MCP 客户端独立的会话。所有客户端入口均使用租约；不再提供共享 `CreateClient`。

查询和普通/批量写命令接收 SDK 注入的取消令牌，CLI Ctrl+C 与 Web 请求取消也传入同一路径；令牌不出现在工具参数、CLI help 中。取消会停止排队、逐行读取和 batch 后续操作。驱动尚未结束时不会提前归还客户端；取消后客户端会废弃。**已完成的写入不自动回滚，也不会自动重试写命令**。异步 SQL 共享总 deadline；独立队列限额/超时先触发则返回 1009，总 deadline 先触发返回 1011，用户取消优先保留 CLI 130。驱动清理可能超过 deadline，服务端等待清理完成后才释放租赁。

能力与已验证边界见 [执行行为与验证记录](Doc/execution-optimization.md)。特别注意：本地客户端停止等待不代表已向服务端发送 MCP 取消通知，也不代表数据库已停止执行。

## 连接与配置（9）

`get_database_capabilities [databaseName]` 离线返回产品能力、配置版本和连接设置。使用 `test_connection` 验证连通性。

细分能力位于 `capabilityDetails.schemaSections`、`schemaOperations` 和 `cliExport`。`provider-dependent` 表示由实际驱动提供支持。JSON help/list 的 `unsupportedProviders` 列出已知排除项。

PostgreSQL 配置诊断包含 `connectionOptions.cancellationTimeoutMs` 和配置来源。Npgsql 的 Cancellation Timeout 单位是毫秒，默认 2000，0 表示无限等待取消响应，-1 表示跳过该等待；它不等于 SQL 执行的 command timeout，也不是整个取消流程的严格上限。工具不自动覆盖连接字符串选项。参见 [Npgsql 官方连接参数](https://www.npgsql.org/doc/connection-string-parameters.html)。

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `test_connection` | `databaseName?`、`maxRetries=0`、`initialDelayMs=1000`、`totalTimeoutSeconds=30` | 固定目标测试；仅暂态错误重试，退避释放执行名额。 | `DatabaseMcpServer tool test_connection --database-name 'reporting'` | 否 |
| `get_database_capabilities` | `databaseName? / --database-name` | 离线报告指定库的能力和预算。 | `DatabaseMcpServer tool get_database_capabilities --database-name 'reporting'` | 否 |
| `get_database_config` | 无 | 返回当前配置摘要和掩码后的连接字符串。 | `DatabaseMcpServer tool get_database_config` | 否 |
| `validate_configuration` | 无 | 验证配置是否能生成可用连接。 | `DatabaseMcpServer tool validate_configuration` | 否 |
| `reload_database_config` | 无 | 重新加载配置并尽量保留当前连接；CLI 状态按配置路径持久化。 | `DatabaseMcpServer tool reload_database_config` | 否 |
| `list_databases` | 无 | 列出全部连接及 default/current 标记。 | `DatabaseMcpServer tool list_databases` | 否 |
| `switch_database` | `databaseName / --database-name` | 切换运行时当前连接；不会修改 `databases.json` 的默认项。 | `DatabaseMcpServer tool switch_database --database-name 'reporting'` | 否 |
| `get_current_database` | 无 | 返回实际用于 tool 调用的当前连接。 | `DatabaseMcpServer tool get_current_database` | 否 |
| `health_check` | 无 | 依次测试所有已配置连接并返回耗时和错误；MCP 客户端请求进度时报告完成数。 | `DatabaseMcpServer tool health_check` | 否 |

## 查询（5）

SQL 参数统一为 `parameters` 对象数组，字段为 `name/value/dbType/direction/size/precision/scale`。NULL 要求 dbType；IN 数组通过 value 传递，不声明 dbType。不接受 JSON 对象或二次编码的 JSON 字符串。

`query_database` 默认 1000 行、1 MiB 工具 JSON、30 秒；`query_table_page` 默认 100 行，按主键排序，不做隐式 COUNT。多结果与 batch 共用 reader 路径，默认也填充行数和字节预算，按调用累计；`DB_QUERY_BOUNDED_DEFAULTS=false` 显式保留这两个入口省略预算的旧行为。配置含义详见[工作流](Doc/database-mcp-workflow.md)。

- 字节预算计量工具 JSON 的 UTF-8 内容，包含元数据和转义，不含 MCP/HTTP 封装。标准错误响应不受成功结果预算限制。
- `query_database` 最少 1024 字节，多结果集最少 256 字节，batch 最少 `256 + 768 × 查询数` 字节。
- `limitReached=true,truncated=null` 表示达到行数上限，未读取下一行；`truncated=true` 表示已丢弃放不下的完整行。batch 未执行项为 `executed=false`。
- 多结果集支持 SQLite/MySQL/PostgreSQL/SQL Server，其他 provider 返回 UnsupportedCapability。有限结果共用字段预算读取：SQLite 在复制 text/blob 前检查原生字节数；PostgreSQL/SQL Server 文本分块读取；二进制优先长度预检。MySQL 文本和未验证驱动仍可能完整物化；驱动行缓存与数据库内存不受响应预算控制。

`get_table_schema.sections.items.enum` 由类型声明生成。MCP、CLI 和 Web 在绑定时校验 section。

| Tool | 主要参数（MCP / CLI） | 使用说明 | CLI 示例 |
| --- | --- | --- | --- |
| `query_database` | `sql / --sql`、`parameters / --parameters`、`databaseName / --database-name`、`maxRows / --max-rows`、`maxResponseBytes / --max-response-bytes` | 通用只读查询；返回 structuredContent/outputSchema、实际目标、版本与预算状态。支持 IN 数组。 | `DatabaseMcpServer tool query_database --sql 'select * from users where id in (@ids)' --parameters '[{"name":"ids","value":[1,2,3]}]'` |
| `query_table_page` | `tableName`、`page=1`、`pageSize=100`、`cursor?`、`columns?`、`filters?`、`orderBy?`、`maxResponseBytes?` | 要求非空主键；省略新增选项时按主键顺序生成 nextCursor。指定选列、筛选或排序时用 offset 页码，不返回 cursor；实际排序追加缺失主键。 | `DatabaseMcpServer tool query_table_page --table-name 'users' --columns '["id","name"]' --filters '[{"column":"id","op":"gt","value":10}]' --order-by '[{"column":"id","direction":"desc"}]' --page-size 100` |
| `get_data_set_all` | `sql / --sql`、`parameters / --parameters`、`maxRows / --max-rows`、`maxResponseBytes / --max-response-bytes` | 多个 SELECT，返回 resultSets。 | `DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --max-rows 1000` |
| `batch_sql_query` | `commands`、`maxRows?`、`maxResponseBytes?` | 1–5 项 `{sql,parameters?}`，共享预算、逐项状态；不接受 queries。 | `DatabaseMcpServer tool batch_sql_query --commands '[{"sql":"select count(*) as total from users"},{"sql":"select count(*) as total from roles"}]'` |
| `explain_query` | `sql`、`parameters?`、`databaseName?`、`maxResponseBytes?`、`commandTimeoutSeconds?` | MySQL / SQL Server / PostgreSQL / SQLite 估算计划，不执行查询；SQL Server 仅应用标量参数类型。 | `DatabaseMcpServer tool explain_query --sql 'select * from users where id=1'` |

以上工具均支持 `databaseName` 和 `commandTimeoutSeconds`，CLI 无需 `--yes`。

选列最多 64 项，筛选最多 32 项 AND 条件，排序最多 8 项。字段名区分大小写且必须存在；筛选支持 `eq/ne/gt/gte/lt/lte/isNull/isNotNull`，比较值可通过 `dbType` 指定标量转换，空值判断省略 value/dbType。排序为 asc/desc，默认 asc。即使传空 filters/orderBy 也进入 offset 模式，不能与 cursor 同用；`effectiveOrderBy` 返回实际排序方向。该分支整页物化后检查响应字节预算，不限制读取期间内存。

不传新增选项时，nextCursor 支持相同配置内容/路径、工作目录、目标库和页大小下跨 CLI 进程续页，不能与 page>1 同用。两种模式均不执行 COUNT，也不提供跨页快照。

## 数据写入与存储过程（5）

这些工具均支持 `databaseName`、`commandTimeoutSeconds`，CLI 必须追加 `--yes`。

| Tool | 主要参数（MCP / CLI） | 使用说明 | CLI 示例 |
| --- | --- | --- | --- |
| `insert_row` | `tableName / --table-name`、`row / --row` | SQL Server/MySQL/SQLite 单行插入，返回 identityColumn 和十进制字符串 identity。 | `DatabaseMcpServer tool insert_row --table-name 'users' --row '{"name":"Alice","email":null}' --yes` |
| `execute_command` | `sql / --sql`、`parameters / --parameters` | INSERT/UPDATE/DELETE，类型化参数，返回 affectedRows。 | `DatabaseMcpServer tool execute_command --sql 'update users set status=@status where id=@id' --parameters '[{"name":"status","value":"active"},{"name":"id","value":1}]' --yes` |
| `call_stored_procedure` | `procedureName / --procedure-name`、`parameters / --parameters`、`maxResponseBytes / --max-response-bytes` | 统一输入/输出/InputOutput/ReturnValue；输出必须指定 dbType，字符串/二进制输出指定 size。MySQL/SQL Server；完整消费 reader 后返回 data/outputParameters。达到行数/字节预算返回 1013，不返回不完整输出参数，副作用不会回滚。 | `DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_user_statistics' --parameters '[{"name":"UserId","value":1001},{"name":"TotalOrders","dbType":"Int32","direction":"Output"}]' --yes` |
| `execute_command_with_go` | `sql / --sql` | 含独立 GO 行的 SQL Server 脚本，SQL 作为单个多行参数传入。 | `DatabaseMcpServer tool execute_command_with_go --sql '<含换行的 GO 脚本>' --yes` |
| `batch_execute_commands` | `commands / --commands` | 非空 `{sql,parameters?}` 对象数组，默认逐条执行且允许部分成功；atomic=true 启用单次事务。 | `DatabaseMcpServer tool batch_execute_commands --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"delete from sessions where user_id=@id","parameters":[{"name":"id","value":1}]}]' --yes` |

`insert_row` 要求唯一且可被 SqlSugar 识别的整数自增列；SQLite 需要 AUTOINCREMENT。row 为 1–128 字段、最多 4 MiB 的对象，值仅 string/bool/number/null，字段名区分大小写，禁止指定自增列。省略列交给数据库默认值处理。仅接受普通表名，SQL Server 跨 schema 同名表会拒绝；PostgreSQL、显式类型、二进制或 schema 限定目标使用参数化 `execute_command`。取消不保证回滚，不能自动重试。

### `execute_command_with_go` PowerShell 示例

```powershell
$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@

DatabaseMcpServer tool execute_command_with_go --sql $sql --yes --config 'D:\config\databases.json'
```

`batch_execute_commands --atomic true` 可显式启用 SQLite / SQL Server / PostgreSQL 事务；默认仍允许部分成功。最多 100 项普通 DML，`totalTimeoutSeconds` 约束整批。必须检查 `transactionOutcome`，unknown 不可自动重放；详见[事务契约](Doc/tool-evolution.md)。

非原子批次返回超时 `1011` 时，检查 `data.results` 里的已知结果。先核对 `data.unknownCommandIndex` 指向的命令是否写入，再决定如何继续。`data.notExecutedFromIndex` 标记尚未执行的尾段。索引从 `0` 开始。

## MongoDB 集合（6）

通过 SqlSugar.MongoDbCore 内置 ADO 执行。输入接受 JSON / Extended JSON v2，输出为 provider 转换后的 JSON，不保证 BSON 无损往返：顶层 ObjectId 为字符串，缺失字段和显式 null 均返回 null。所有工具支持 `databaseName`、`commandTimeoutSeconds`、`maxResponseBytes`。默认 30 秒、1 MiB；find/aggregate 默认最多 1000 条。Mongo 不支持通用 SQL 工具，反向调用同样返回 1012。

| Tool | 主要参数 | 结果 | `--yes` |
| --- | --- | --- | --- |
| `mongo_find` | `collection`、`filter?`、`projection?`、`sort?`、`skip=0`、`maxRows?` | documents、limitReached、truncated | 否 |
| `mongo_aggregate` | `collection`、`pipeline`、`maxRows?` | 只读 stage 白名单，documents、limitReached、truncated | 否 |
| `mongo_insert_many` | `collection`、`documents`（1–1000 个对象） | insertedCount、writeOutcome；不返回 insertedId | 是 |
| `mongo_update` | `collection`、`filter`、`update`、`allowAll=false` | 批量更新，modifiedCount、writeOutcome | 是 |
| `mongo_delete` | `collection`、`filter`、`allowAll=false` | 批量删除，deletedCount、writeOutcome | 是 |
| `mongo_truncate` | `collection` | 清空文档、保留集合和索引，writeOutcome；要求 schema-admin | 是 |

MCP 直接传对象；CLI 如 `--filter '{"_id":"order-42"}'`。update/delete 始终批量执行且要求 `enableDangerousOperations=true`，空 filter 还需要 `allowAll=true`；truncate 同样要求危险操作开关。many/upsert 参数已移除。URI 必须指定业务库，当前拒绝 mongodb+srv。读取先限制服务器返回行数，再由 provider 物化后检查字节预算；初始同步查询可能延迟取消。取消或 writeOutcome=unknown 后先核实数据。迁移、类型转换和能力边界见[完整契约](Doc/mongodb-collections.md)。

## Schema 查询与检查（8）

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `get_data_base_list` | 无 | 返回当前实例可见的数据库列表。 | `DatabaseMcpServer tool get_data_base_list` | 否 |
| `is_any_table` | `tableName / --table-name` | 判断表是否存在，返回 `exists`。 | `DatabaseMcpServer tool is_any_table --table-name 'users'` | 否 |
| `is_any_column` | `tableName / --table-name`、`columnName / --column-name` | 判断列是否存在，返回 `exists`。 | `DatabaseMcpServer tool is_any_column --table-name 'users' --column-name 'email'` | 否 |
| `is_any_constraint` | `constraintName / --constraint-name` | 判断唯一、外键、检查等约束是否存在。 | `DatabaseMcpServer tool is_any_constraint --constraint-name 'PK_users'` | 否 |
| `is_any_table_remark` | `tableName / --table-name` | 判断表是否已有描述。 | `DatabaseMcpServer tool is_any_table_remark --table-name 'users'` | 否 |
| `get_table_schema` | `tableName` 或 `tableNames`、`sections?`、`refresh=false`、`maxRows?`、`maxResponseBytes?`、`commandTimeoutSeconds?` | 异步读取；最多 50 张表，共享预算、逐表状态，缓存命中也计量。SQLite/MySQL/PostgreSQL/SQL Server/DuckDB。 | `DatabaseMcpServer tool get_table_schema --table-names '["users","roles"]' --sections '["columns","foreignKeys"]'` | 否 |
| `list_objects` | `kind`、`schema?`、`namePrefix?`、`tableName?`、`cursor?`、`limit=100` | 原生目录或 SqlSugar provider 目录；pagingMode 指明 keyset/client，cursor 绑定查询条件、配置内容/路径、工作目录及目标库，可跨 CLI 进程续页。 | `DatabaseMcpServer tool list_objects --kind 'table' --limit 50` | 否 |
| `get_object_definition` | `kind`、`name`、`schema?` | 定义与 availability；不可见/缺失/加密不猜原因。 | `DatabaseMcpServer tool get_object_definition --kind 'view' --name 'active_users'` | 否 |

## Schema 变更（21）

这些工具会修改 schema，CLI 必须追加 `--yes`。建议先用上一节的查询工具确认对象状态，并在非业务对象上验证。

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `create_table` | `tableName / --table-name`、`columns / --columns`、`[isCreatePrimaryKey / --is-create-primary-key]` | 根据列定义 JSON 数组创建表；常用字段包括 `name`、`dataType`、`length`、`nullable`、`primaryKey`、`identity`。 | `DatabaseMcpServer tool create_table --table-name 'users' --columns '[{"name":"id","dataType":"int","primaryKey":true,"identity":true},{"name":"name","dataType":"nvarchar","length":100}]' --yes` | 是 |
| `drop_table` | `tableName / --table-name` | 删除表结构和全部数据。 | `DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes` | 是 |
| `truncate_table` | `tableName / --table-name` | 清空全部数据并保留表结构。 | `DatabaseMcpServer tool truncate_table --table-name 'temp_users' --yes` | 是 |
| `backup_table` | `oldTableName / --old-table-name`、`newTableName / --new-table-name` | 复制表结构和当前数据。 | `DatabaseMcpServer tool backup_table --old-table-name 'users' --new-table-name 'users_backup' --yes` | 是 |
| `rename_table` | `oldTableName / --old-table-name`、`newTableName / --new-table-name` | 重命名表。 | `DatabaseMcpServer tool rename_table --old-table-name 'users_tmp' --new-table-name 'users_archive' --yes` | 是 |
| `add_column` | `tableName / --table-name`、`column / --column` | 根据 JSON 定义添加列；常用字段包括 `name`、`dataType`、`length`、`nullable`、`scale`。 | `DatabaseMcpServer tool add_column --table-name 'users' --column '{"name":"mobile","dataType":"nvarchar","length":20,"nullable":true}' --yes` | 是 |
| `update_column` | `tableName / --table-name`、`column / --column` | 根据完整 JSON 定义修改现有列。 | `DatabaseMcpServer tool update_column --table-name 'users' --column '{"name":"mobile","dataType":"nvarchar","length":30,"nullable":true}' --yes` | 是 |
| `drop_column` | `tableName / --table-name`、`columnName / --column-name` | 删除指定列。 | `DatabaseMcpServer tool drop_column --table-name 'users' --column-name 'mobile' --yes` | 是 |
| `rename_column` | `tableName / --table-name`、`oldColumnName / --old-column-name`、`newColumnName / --new-column-name` | 重命名列。 | `DatabaseMcpServer tool rename_column --table-name 'users' --old-column-name 'mobile' --new-column-name 'phone' --yes` | 是 |
| `add_primary_key` | `tableName`、`columnName` 或 `columnNames`（二选一） | 创建单列或复合主键，最多 32 列；受 provider 能力限制。 | `DatabaseMcpServer tool add_primary_key --table-name 'order_items' --column-names '["order_id","item_id"]' --yes` | 是 |
| `drop_constraint` | `tableName / --table-name`、`constraintName / --constraint-name` | 删除主键、唯一、外键等约束。 | `DatabaseMcpServer tool drop_constraint --table-name 'users' --constraint-name 'PK_users' --yes` | 是 |
| `create_index` | `tableName`、`indexName`、`columnName` 或 `columnNames`（二选一）、`isUnique=false` | 创建单列或多列索引，最多 32 列。 | `DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_status_id' --column-names '["status","id"]' --yes` | 是 |
| `drop_index` | `tableName`、`indexName` | 删除指定表的索引，受 provider 能力限制。 | `DatabaseMcpServer tool drop_index --table-name 'users' --index-name 'IX_users_status_id' --yes` | 是 |
| `add_default_value` | `tableName / --table-name`、`columnName / --column-name`、`defaultValue / --default-value` | 设置默认值；SQL Server 文本值必须传 SQL literal。 | `DatabaseMcpServer tool add_default_value --table-name 'users' --column-name 'status' --default-value '''active''' --yes` | 是 |
| `add_table_remark` | `tableName / --table-name`、`description / --description` | 添加表描述。 | `DatabaseMcpServer tool add_table_remark --table-name 'users' --description '用户表' --yes` | 是 |
| `delete_table_remark` | `tableName / --table-name` | 删除表描述。 | `DatabaseMcpServer tool delete_table_remark --table-name 'users' --yes` | 是 |
| `add_column_remark` | `tableName / --table-name`、`columnName / --column-name`、`description / --description` | 添加列描述；SQL Server 使用扩展属性。 | `DatabaseMcpServer tool add_column_remark --table-name 'users' --column-name 'email' --description '邮箱地址' --yes` | 是 |
| `delete_column_remark` | `tableName / --table-name`、`columnName / --column-name` | 删除列描述。 | `DatabaseMcpServer tool delete_column_remark --table-name 'users' --column-name 'email' --yes` | 是 |
| `drop_view` | `viewName / --view-name` | 删除视图。 | `DatabaseMcpServer tool drop_view --view-name 'v_active_users' --yes` | 是 |
| `drop_func` | `functionName / --function-name` | 删除函数。 | `DatabaseMcpServer tool drop_func --function-name 'fn_calc_score' --yes` | 是 |
| `drop_proc` | `procedureName / --procedure-name` | 删除存储过程。 | `DatabaseMcpServer tool drop_proc --procedure-name 'sp_cleanup_logs' --yes` | 是 |

## 返回与错误处理

- 成功调用通常返回 JSON，顶层包含 `success: true`。
- 工具执行失败通常返回 `success: false`、`errorCode`、`errorMessage`；CLI 退出码为 `1`。
- CLI 缺少参数、JSON 无法解析或缺少 `--yes` 时，工具不会执行，退出码为 `2`。
- `batch_sql_query` 和 `batch_execute_commands` 的顶层调用可以成功，但单项结果仍可能是 `success: false`，调用方必须检查每个 `results[]`。
- `get_database_config` 成功时可能没有顶层 `success`；以 CLI 退出码 `0` 且 stdout 为合法 JSON 作为成功依据。

## 工具统计

- 连接、配置与运行诊断：9
- SQL 查询：5
- 数据写入与存储过程：5
- MongoDB 集合：6
- Schema 查询与检查：8
- Schema 变更：21

**静态总计：54**。实际目录依配置筛选，CLI export、MCP Resources 和 Prompts 不计入工具数。已删除工具无别名；单行/标量查询统一使用 `query_database`，需要首行时指定 `maxRows=1`，聚合列显式命名，结果读取 `data[0]` 并检查空数组与截断状态。

## MCP Resources、Prompts 与补全

MCP stdio 同时提供三类 JSON 资源：`db://connections`、`db://capabilities/{databaseName}`、`db://schema/{databaseName}/{tableName}`，分别复用连接列表、离线能力和表结构操作。Prompt 为 `analyze_slow_query(sql, databaseName?)` 与 `safe_schema_migration(tableName, changeGoal, databaseName?)`，只返回指导步骤，不自动执行迁移。

连接名补全支持 databaseName/database_name 前缀匹配，最多返回 100 项并报告 total/hasMore。服务端提供使用说明；health_check 在客户端请求进度时报告逐连接完成数。这些协议入口不作为 CLI tool 名称调用。

## 相关文档

- [Doc/cli.md](Doc/cli.md) - CLI 安装、配置、PowerShell quoting、退出码和工作流
- [README.md](README.md) - 项目概述与快速开始
- [DatabaseSetting/](DatabaseSetting/) - 数据库连接配置示例

## CLI 流式导出

`DatabaseMcpServer export --sql 'SELECT id,name FROM users' --format 'jsonl' --output 'D:\exports\users.jsonl' --database-name 'reporting'`。

仅接受只读 SQL，支持 typed `--parameters`。默认 100000 行、256 MiB 文件、30 秒总 deadline，均受服务端策略约束；`--max-file-mib` 为 1–1024。逐行读取到同目录临时文件，成功后发布；默认不覆盖，显式 `--overwrite` 才替换。stdout 仅含摘要；触及行数上限 `limitReached=true` 不能断言已完整导出。CSV 保留原值（包括公式前缀）、NULL 为空；JSONL 保留类型。失败保留已有目标文件并清理临时文件。
