# DatabaseMcpServer Tools 使用手册

本文档以当前源码 catalog 为准，覆盖全部 **55 个** MCP tools。CLI 的安装、配置发现、退出码和完整工作流见 [Doc/cli.md](Doc/cli.md)。

## 调用约定

- MCP tool 名统一使用 `snake_case`；MCP 参数使用源码中的 `camelCase`。
- CLI 调用格式：`DatabaseMcpServer tool <tool_name> [options]`，参数使用 `--kebab-case`。
- 下表的参数格式为 `MCP 参数 / CLI 参数`；方括号表示可选参数。
- CLI 示例省略 `--config` 时会按 `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json` 查找配置。自动化场景建议显式追加 `--config 'D:\config\databases.json'`。
- 标记为“是”的工具会修改数据或 schema，CLI 必须追加 `--yes`。
- PowerShell 中 SQL 和 JSON 参数应使用单引号包裹。调用前可运行 `DatabaseMcpServer tool help <tool_name>` 查看实时参数。
- 查询与数据操作类工具支持可选 `commandTimeoutSeconds / --command-timeout-seconds`：单位秒，省略时使用驱动默认（通常 300），`0` 表示无限等待，合法范围 `0–86400`。

## 连接与配置（10）

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

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `sql_query` | `sql / --sql`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 执行单条只读 SQL；可使用 JSON 对象绑定参数，返回 `rowCount` 和 `data`。可选超时单位为秒，省略时使用驱动默认（通常 300）；`0` 表示无限等待。 | `DatabaseMcpServer tool sql_query --sql 'select * from users where status=@status' --parameters '{"status":"active"}' --command-timeout-seconds 600` | 否 |
| `sql_query_single` | `sql / --sql`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 执行只读 SQL，仅返回首行或 `null`。 | `DatabaseMcpServer tool sql_query_single --sql 'select * from users where id=@id' --parameters '{"id":1}'` | 否 |
| `get_data_set_all` | `sql / --sql`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 执行包含多个 SELECT 的 SQL，返回多个 `resultSets`。 | `DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles'` | 否 |
| `get_scalar` | `sql / --sql`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 返回首行首列，适合 `COUNT/SUM/MAX`。 | `DatabaseMcpServer tool get_scalar --sql 'select count(*) from users'` | 否 |
| `sql_query_with_in_parameter` | `sql / --sql`、`inParameterName / --in-parameter-name`、`inValues / --in-values`、`[otherParameters / --other-parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 安全绑定 IN 数组，并可附加其他参数。 | `DatabaseMcpServer tool sql_query_with_in_parameter --sql 'select * from users where id in (@ids)' --in-parameter-name 'ids' --in-values '[1,2,3]'` | 否 |
| `batch_sql_query` | `queries / --queries`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 在同一连接顺序执行 1–5 条只读 SQL；逐条返回成功结果或错误，单条失败不阻止后续查询。超时作用于批内每条查询。 | `DatabaseMcpServer tool batch_sql_query --queries '["select count(*) from users","select count(*) from roles"]'` | 否 |

## 数据写入与存储过程（5）

这些工具会修改数据或调用可能产生副作用的存储过程。CLI 必须追加 `--yes`。

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `execute_command` | `sql / --sql`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 执行单条 INSERT/UPDATE/DELETE，返回 `affectedRows`。可选超时单位为秒，省略时使用驱动默认（通常 300）；`0` 表示无限等待。 | `DatabaseMcpServer tool execute_command --sql 'update users set status=@status where id=@id' --parameters '{"status":"active","id":1}' --command-timeout-seconds 900 --yes` | 是 |
| `call_stored_procedure` | `procedureName / --procedure-name`、`[parameters / --parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 调用存储过程并返回结果集和行数。 | `DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_monthly_report' --parameters '{"year":2026,"month":7}' --yes` | 是 |
| `call_stored_procedure_with_output` | `procedureName / --procedure-name`、`[inputParameters / --input-parameters]`、`[outputParameters / --output-parameters]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 调用带输出参数的存储过程，返回结果集和输出值。 | `DatabaseMcpServer tool call_stored_procedure_with_output --procedure-name 'sp_user_statistics' --input-parameters '{"UserId":1001}' --output-parameters '["TotalOrders"]' --yes` | 是 |
| `execute_command_with_go` | `sql / --sql`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 执行含独立 `GO` 行的 SQL Server 脚本并汇总影响行数；SQL 必须作为单个多行参数传入。 | `DatabaseMcpServer tool execute_command_with_go --sql "<含换行的 GO 脚本>" --yes` | 是 |
| `batch_execute_commands` | `commands / --commands`、`[parametersArray / --parameters-array]`、`[commandTimeoutSeconds / --command-timeout-seconds]` | 同一连接顺序执行命令数组，每条可绑定独立参数并独立返回结果；当前不提供事务，允许部分成功。超时作用于批内每条命令。 | `DatabaseMcpServer tool batch_execute_commands --commands '["update users set status=@status where id=@id","delete from sessions where user_id=@id"]' --parameters-array '[{"status":"active","id":1},{"id":1}]' --yes` | 是 |

### `execute_command_with_go` PowerShell 示例

```powershell
$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@

DatabaseMcpServer tool execute_command_with_go --sql $sql --yes --config 'D:\config\databases.json'
```

## Schema 查询与检查（15）

| Tool | 参数（MCP / CLI） | 使用说明 | CLI 示例 | `--yes` |
| --- | --- | --- | --- | --- |
| `get_data_base_list` | 无 | 返回当前实例可见的数据库列表。 | `DatabaseMcpServer tool get_data_base_list` | 否 |
| `get_view_info_list` | 无 | 返回视图名称、定义和 schema 信息。 | `DatabaseMcpServer tool get_view_info_list` | 否 |
| `get_table_info_list` | 无 | 返回表名、描述、创建时间等基本信息。 | `DatabaseMcpServer tool get_table_info_list` | 否 |
| `get_column_infos_by_table_name` | `tableName / --table-name` | 返回指定表的列类型、长度、可空等元数据。 | `DatabaseMcpServer tool get_column_infos_by_table_name --table-name 'users'` | 否 |
| `get_is_identities` | `tableName / --table-name` | 返回指定表的自增列。 | `DatabaseMcpServer tool get_is_identities --table-name 'users'` | 否 |
| `get_primaries` | `tableName / --table-name` | 返回主键约束、列和组合主键顺序。 | `DatabaseMcpServer tool get_primaries --table-name 'users'` | 否 |
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

- 连接与配置：10
- 查询：6
- 数据写入与存储过程：5
- Schema 查询与检查：15
- Schema 变更：20

**总计：56**

## 相关文档

- [Doc/cli.md](Doc/cli.md) - CLI 安装、配置、PowerShell quoting、退出码和工作流
- [README.md](README.md) - 项目概述与快速开始
- [DatabaseSetting/](DatabaseSetting/) - 数据库连接配置示例
