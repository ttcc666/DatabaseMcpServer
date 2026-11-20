# DatabaseMcpServer Tools 使用手册

本文档以当前源码 catalog 为准，包含 **62 个** MCP tools。类型化 SQL 查询、分页、命名目标、权限和预算见 [数据库 MCP 工作流](Doc/database-mcp-workflow.md)，MongoDB 见[集合契约](Doc/mongodb-collections.md)。CLI 的安装、配置发现、退出码和完整工作流见 [Doc/cli.md](Doc/cli.md)。

## 调用约定

- MCP tool 名统一使用 `snake_case`；MCP 参数使用源码中的 `camelCase`。
- Query、Command、Schema 工具均支持可选 `databaseName / --database-name`，推荐每次显式选库，不修改 currentDatabase。
- CLI 调用格式：`DatabaseMcpServer tool <tool_name> [options]`，参数使用 `--kebab-case`。
- 下表的参数格式为 `MCP 参数 / CLI 参数`；方括号表示可选参数。
- CLI 示例省略 `--config` 时会按 `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json` 查找配置。自动化场景建议显式追加 `--config 'D:\config\databases.json'`。
- 标记为“是”的工具会修改数据或 schema，CLI 必须追加 `--yes`。
- PowerShell 中 SQL 和 JSON 参数应使用单引号包裹。调用前可运行 `DatabaseMcpServer tool help <tool_name>` 查看实时参数。
- 查询与数据操作类工具支持可选 `commandTimeoutSeconds / --command-timeout-seconds`：单位秒，`query_database` 默认 30 秒，其他入口由执行策略配置决定（未启用默认预算时通常 300 秒），`0` 表示无限等待，合法范围 `0–86400`。

## 执行上限与取消

MCP、CLI、Web 的数据库工具共用独占客户端租约，按配置连接名称限制并发。默认每项最多执行 **8** 个请求、排队 **32** 个请求、最多等待 **30 秒**；队满或等待超时返回 `errorCode=1009`（DatabaseBusy）。启动前可用 `DB_MAX_CONCURRENT`（1–1024）、`DB_MAX_QUEUED`（0–10000）、`DB_QUEUE_TIMEOUT_SECONDS`（1–86400）调整，非法配置会拒绝启动。

请求在排队前固定数据库和权限快照；切库、重载只影响之后接纳的请求。重载和重命名继续共享原并发额度；独立配置项即使指向同一物理数据库也分别计算。当前数据库是服务共享的选择，并非每个 MCP 客户端独立的会话。所有客户端入口均使用租约；不再提供共享 `CreateClient`。

查询和普通/批量写命令接收 SDK 注入的取消令牌，CLI Ctrl+C 与 Web 请求取消也传入同一路径；令牌不出现在工具参数、CLI help 中。取消会停止排队、逐行读取和 batch 后续操作。驱动尚未结束时不会提前归还客户端；取消后客户端会废弃。**已完成的写入不自动回滚，也不会自动重试写命令**。SQL command timeout 与排队超时独立。

能力与已验证边界见 [执行行为与验证记录](Doc/execution-optimization.md)。特别注意：本地客户端停止等待不代表已向服务端发送 MCP 取消通知，也不代表数据库已停止执行。

## 连接与配置（11）

新增 `get_database_capabilities [databaseName]` 返回真实能力、配置版本和配置诊断。

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `test_connection` | 无 | 验证当前连接且不执行方言相关测试 SQL，返回连接状态、当前连接名和数据库类型。 | `DatabaseMcpServer tool test_connection` | 否 |
| `test_connection_by_name` | `databaseName / --database-name` | 测试指定配置项，不切换当前连接。 | `DatabaseMcpServer tool test_connection_by_name --database-name 'reporting'` | 否 |
| `get_database_config` | 无 | 返回当前配置摘要和掩码后的连接字符串。 | `DatabaseMcpServer tool get_database_config` | 否 |
| `validate_configuration` | 无 | 验证配置是否能生成可用连接。 | `DatabaseMcpServer tool validate_configuration` | 否 |
| `reload_database_config` | 无 | 重新加载配置并尽量保留当前连接；CLI 状态按配置路径持久化。 | `DatabaseMcpServer tool reload_database_config` | 否 |
| `list_databases` | 无 | 列出全部连接及 default/current 标记。 | `DatabaseMcpServer tool list_databases` | 否 |
| `switch_database` | `databaseName / --database-name` | 切换运行时当前连接；不会修改 `databases.json` 的默认项。 | `DatabaseMcpServer tool switch_database --database-name 'reporting'` | 否 |
| `get_current_database` | 无 | 返回实际用于 tool 调用的当前连接。 | `DatabaseMcpServer tool get_current_database` | 否 |
| `health_check` | 无 | 依次测试所有已配置连接并返回耗时和错误。 | `DatabaseMcpServer tool health_check` | 否 |
| `test_connection_with_retry` | `[maxRetries / --max-retries]`、`[initialDelayMs / --initial-delay-ms]` | 按指数退避重试；默认重试 3 次、初始延迟 1000ms。 | `DatabaseMcpServer tool test_connection_with_retry --max-retries 3 --initial-delay-ms 1000` | 否 |

## 查询（6）

SQL 参数统一为 `parameters` 对象数组，字段为 `name/value/dbType/direction/size/precision/scale`。NULL 要求 dbType；IN 数组通过 value 传递，不声明 dbType。不接受 JSON 对象或二次编码的 JSON 字符串。

`query_database` 默认 1000 行、1 MiB 工具 JSON、30 秒；`query_table_page` 默认 100 行，按主键排序，不做隐式 COUNT。多结果与 batch 共用 reader 路径，预算按调用累计；`DB_QUERY_BOUNDED_DEFAULTS` 控制它们省略预算时是否填充默认值。配置含义详见[工作流](Doc/database-mcp-workflow.md)。

- 字节预算计量工具 JSON 的 UTF-8 内容，包含元数据和转义，不含 MCP/HTTP 封装。标准错误响应不受成功结果预算限制。
- `query_database` 最少 1024 字节，多结果集最少 256 字节，batch 最少 `256 + 768 × 查询数` 字节。
- `limitReached=true,truncated=null` 表示达到行数上限，未读取下一行；`truncated=true` 表示已丢弃放不下的完整行。batch 未执行项为 `executed=false`。
- 多结果集支持 SQLite/MySQL/PostgreSQL/SQL Server，其他 provider 返回 UnsupportedCapability。驱动仍可能物化单个大字段；响应预算不是进程内存或数据库计算量上限。

| Tool | 主要参数（MCP / CLI） | 使用说明 | CLI 示例 |
| --- | --- | --- | --- |
| `query_database` | `sql / --sql`、`parameters / --parameters`、`databaseName / --database-name`、`maxRows / --max-rows`、`maxResponseBytes / --max-response-bytes` | 通用只读查询；返回 structuredContent/outputSchema、实际目标、版本与预算状态。支持 IN 数组。 | `DatabaseMcpServer tool query_database --sql 'select * from users where id in (@ids)' --parameters '[{"name":"ids","value":[1,2,3]}]'` |
| `query_table_page` | `tableName / --table-name`、`page / --page`、`pageSize / --page-size` | 按主键升序分页；无主键表拒绝执行。 | `DatabaseMcpServer tool query_table_page --table-name 'users' --page 1 --page-size 100` |
| `sql_query_single` | `sql / --sql`、`parameters / --parameters`、`maxResponseBytes / --max-response-bytes` | 只返回首行或 null；超字节预算报错，不截断单值。 | `DatabaseMcpServer tool sql_query_single --sql 'select * from users where id=@id' --parameters '[{"name":"id","value":1}]'` |
| `get_data_set_all` | `sql / --sql`、`parameters / --parameters`、`maxRows / --max-rows`、`maxResponseBytes / --max-response-bytes` | 多个 SELECT，返回 resultSets。 | `DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --max-rows 1000` |
| `get_scalar` | `sql / --sql`、`parameters / --parameters`、`maxResponseBytes / --max-response-bytes` | 返回首行首列，适合 COUNT/SUM/MAX。 | `DatabaseMcpServer tool get_scalar --sql 'select count(*) from users'` |
| `batch_sql_query` | `queries / --queries`、`maxRows / --max-rows`、`maxResponseBytes / --max-response-bytes` | 1–5 条只读 SQL 字符串数组；逐条结果，允许部分成功。 | `DatabaseMcpServer tool batch_sql_query --queries '["select count(*) from users","select count(*) from roles"]'` |

以上工具均支持 `databaseName` 和 `commandTimeoutSeconds`，CLI 无需 `--yes`。

## 数据写入与存储过程（4）

这些工具均支持 `databaseName`、`commandTimeoutSeconds`，CLI 必须追加 `--yes`。

| Tool | 主要参数（MCP / CLI） | 使用说明 | CLI 示例 |
| --- | --- | --- | --- |
| `execute_command` | `sql / --sql`、`parameters / --parameters` | INSERT/UPDATE/DELETE，类型化参数，返回 affectedRows。 | `DatabaseMcpServer tool execute_command --sql 'update users set status=@status where id=@id' --parameters '[{"name":"status","value":"active"},{"name":"id","value":1}]' --yes` |
| `call_stored_procedure` | `procedureName / --procedure-name`、`parameters / --parameters`、`maxResponseBytes / --max-response-bytes` | 统一输入/输出/InputOutput/ReturnValue；输出必须指定 dbType，字符串/二进制输出指定 size。返回 data 和 outputParameters。 | `DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_user_statistics' --parameters '[{"name":"UserId","value":1001},{"name":"TotalOrders","dbType":"Int32","direction":"Output"}]' --yes` |
| `execute_command_with_go` | `sql / --sql` | 含独立 GO 行的 SQL Server 脚本，SQL 作为单个多行参数传入。 | `DatabaseMcpServer tool execute_command_with_go --sql '<含换行的 GO 脚本>' --yes` |
| `batch_execute_commands` | `commands / --commands` | 非空 `{sql,parameters?}` 对象数组，逐条执行并报告结果；不是事务，允许部分成功。 | `DatabaseMcpServer tool batch_execute_commands --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"delete from sessions where user_id=@id","parameters":[{"name":"id","value":1}]}]' --yes` |

### `execute_command_with_go` PowerShell 示例

```powershell
$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@

DatabaseMcpServer tool execute_command_with_go --sql $sql --yes --config 'D:\config\databases.json'
```

## MongoDB 集合（6）

使用真实 JSON / Extended JSON v2 对象参数，文档与 ID 返回 Canonical EJSON。所有工具支持 `databaseName`、`commandTimeoutSeconds`、`maxResponseBytes`。默认 30 秒、1 MiB；list/find 默认 1000 条，预算受服务器策略限制。Mongo 不支持通用 SQL 工具，反向调用同样返回 1012。

| Tool | 主要参数 | 结果 | `--yes` |
| --- | --- | --- | --- |
| `mongo_list_collections` | `namePrefix?`、`maxRows?` | collections name/type | 否 |
| `mongo_find` | `collection`、`filter?`、`projection?`、`sort?`、`skip=0`、`maxRows?` | documents、limitReached、truncated | 否 |
| `mongo_count` | `collection`、`filter?` | matchedCount | 否 |
| `mongo_insert_one` | `collection`、`document` | insertedId、writeOutcome | 是 |
| `mongo_update` | `collection`、`filter`、`update`、`many=false`、`upsert=false`、`allowAll=false` | matchedCount、modifiedCount、upsertedId、writeOutcome | 是 |
| `mongo_delete` | `collection`、`filter`、`many=false`、`allowAll=false` | deletedCount、writeOutcome | 是 |

MCP 直接传对象；CLI 如 `--filter '{"_id":"order-42"}'`。`many=true` 要求危险操作开关，空 filter 还需要 `allowAll=true`；many+upsert 不允许。取消或 unknown 写入结果可能已经生效，先核实后再重试。输入限制、操作符白名单、EJSON 和分页语义见[完整契约](Doc/mongodb-collections.md)。

## Schema 查询与检查（15）

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `get_data_base_list` | 无 | 返回当前实例可见的数据库列表。 | `DatabaseMcpServer tool get_data_base_list` | 否 |
| `get_view_info_list` | 无 | 返回新鲜视图基础信息 Name、Description、DbObjectType，不含定义或 schema。 | `DatabaseMcpServer tool get_view_info_list` | 否 |
| `get_table_info_list` | 无 | 返回新鲜表基础信息 Name、Description、DbObjectType，不含创建时间。 | `DatabaseMcpServer tool get_table_info_list` | 否 |
| `get_column_infos_by_table_name` | `tableName / --table-name` | 返回指定表的列类型、长度、可空等元数据。 | `DatabaseMcpServer tool get_column_infos_by_table_name --table-name 'users'` | 否 |
| `get_is_identities` | `tableName / --table-name` | 返回指定表的自增列。 | `DatabaseMcpServer tool get_is_identities --table-name 'users'` | 否 |
| `get_primaries` | `tableName / --table-name` | 返回主键列名数组，不含约束名或组合主键顺序。 | `DatabaseMcpServer tool get_primaries --table-name 'users'` | 否 |
| `is_any_table` | `tableName / --table-name` | 判断表是否存在，返回 `exists`。 | `DatabaseMcpServer tool is_any_table --table-name 'users'` | 否 |
| `is_any_column` | `tableName / --table-name`、`columnName / --column-name` | 判断列是否存在，返回 `exists`。 | `DatabaseMcpServer tool is_any_column --table-name 'users' --column-name 'email'` | 否 |
| `is_any_constraint` | `constraintName / --constraint-name` | 判断唯一、外键、检查等约束是否存在。 | `DatabaseMcpServer tool is_any_constraint --constraint-name 'PK_users'` | 否 |
| `get_index_list` | `tableName / --table-name` | 返回指定表的索引及属性。 | `DatabaseMcpServer tool get_index_list --table-name 'users'` | 否 |
| `is_any_table_remark` | `tableName / --table-name` | 判断表是否已有描述。 | `DatabaseMcpServer tool is_any_table_remark --table-name 'users'` | 否 |
| `get_proc_list` | 无 | 返回当前数据库的存储过程名称。 | `DatabaseMcpServer tool get_proc_list` | 否 |
| `get_func_list` | 无 | 返回当前数据库的函数名称。 | `DatabaseMcpServer tool get_func_list` | 否 |
| `get_trigger_names` | `tableName / --table-name` | 返回指定表的触发器名称。 | `DatabaseMcpServer tool get_trigger_names --table-name 'users'` | 否 |
| `get_table_schema` | `tableName / --table-name` | 汇总返回列、主键、自增列和索引。 | `DatabaseMcpServer tool get_table_schema --table-name 'users'` | 否 |

## Schema 变更（20）

这些工具会修改 schema，CLI 必须追加 `--yes`。建议先用上一节的查询工具确认对象状态，并在非业务对象上验证。

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `create_table` | `tableName / --table-name`、`columnsInfo / --columns-info`、`[isCreatePrimaryKey / --is-create-primary-key]` | 根据列定义 JSON 数组创建表；常用字段包括 `DbColumnName`、`DataType`、`Length`、`IsNullable`、`IsPrimarykey`、`IsIdentity`。 | `DatabaseMcpServer tool create_table --table-name 'users' --columns-info '[{"DbColumnName":"id","DataType":"int","IsPrimarykey":true,"IsIdentity":true},{"DbColumnName":"name","DataType":"nvarchar","Length":100}]' --yes` | 是 |
| `drop_table` | `tableName / --table-name` | 删除表结构和全部数据。 | `DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes` | 是 |
| `truncate_table` | `tableName / --table-name` | 清空全部数据并保留表结构。 | `DatabaseMcpServer tool truncate_table --table-name 'temp_users' --yes` | 是 |
| `backup_table` | `oldTableName / --old-table-name`、`newTableName / --new-table-name` | 复制表结构和当前数据。 | `DatabaseMcpServer tool backup_table --old-table-name 'users' --new-table-name 'users_backup' --yes` | 是 |
| `rename_table` | `oldTableName / --old-table-name`、`newTableName / --new-table-name` | 重命名表。 | `DatabaseMcpServer tool rename_table --old-table-name 'users_tmp' --new-table-name 'users_archive' --yes` | 是 |
| `add_column` | `tableName / --table-name`、`columnInfo / --column-info` | 根据 JSON 定义添加列；常用字段包括 `DbColumnName`、`DataType`、`Length`、`IsNullable`、`DecimalDigits`。 | `DatabaseMcpServer tool add_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":20,"IsNullable":true}' --yes` | 是 |
| `update_column` | `tableName / --table-name`、`columnInfo / --column-info` | 根据完整 JSON 定义修改现有列。 | `DatabaseMcpServer tool update_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":30,"IsNullable":true}' --yes` | 是 |
| `drop_column` | `tableName / --table-name`、`columnName / --column-name` | 删除指定列。 | `DatabaseMcpServer tool drop_column --table-name 'users' --column-name 'mobile' --yes` | 是 |
| `rename_column` | `tableName / --table-name`、`oldColumnName / --old-column-name`、`newColumnName / --new-column-name` | 重命名列。 | `DatabaseMcpServer tool rename_column --table-name 'users' --old-column-name 'mobile' --new-column-name 'phone' --yes` | 是 |
| `add_primary_key` | `tableName / --table-name`、`columnName / --column-name` | 为单列创建主键。 | `DatabaseMcpServer tool add_primary_key --table-name 'users' --column-name 'id' --yes` | 是 |
| `drop_constraint` | `tableName / --table-name`、`constraintName / --constraint-name` | 删除主键、唯一、外键等约束。 | `DatabaseMcpServer tool drop_constraint --table-name 'users' --constraint-name 'PK_users' --yes` | 是 |
| `create_index` | `tableName / --table-name`、`indexName / --index-name`、`columnName / --column-name`、`[isUnique / --is-unique]` | 创建单列索引；`isUnique` 默认 `false`。 | `DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_email' --column-name 'email' --is-unique true --yes` | 是 |
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

- 连接与配置：11
- 查询：8
- 数据写入与存储过程：5
- Schema 查询与检查：15
- Schema 变更：20

**总计：59**

## 相关文档

- [Doc/cli.md](Doc/cli.md) - CLI 安装、配置、PowerShell quoting、退出码和工作流
- [README.md](README.md) - 项目概述与快速开始
- [DatabaseSetting/](DatabaseSetting/) - 数据库连接配置示例
