# DatabaseMcpServer CLI 常用命令

## 基本格式

```powershell
DatabaseMcpServer tool <tool_name> [--option value...] [--config path] [--yes]
```

## 连接与配置

```powershell
DatabaseMcpServer tool validate_configuration --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection_by_name --database-name 'reporting' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_database_config --config 'D:\config\databases.json'
DatabaseMcpServer tool list_databases --config 'D:\config\databases.json'
DatabaseMcpServer tool switch_database --database-name 'reporting' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_current_database --config 'D:\config\databases.json'
DatabaseMcpServer tool health_check --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection_with_retry --max-retries 3 --initial-delay-ms 1000 --config 'D:\config\databases.json'
```

## 架构查询

```powershell
DatabaseMcpServer tool get_table_info_list --config 'D:\config\databases.json'
DatabaseMcpServer tool get_view_info_list --config 'D:\config\databases.json'
DatabaseMcpServer tool get_proc_list --config 'D:\config\databases.json'
DatabaseMcpServer tool get_func_list --config 'D:\config\databases.json'
DatabaseMcpServer tool get_column_infos_by_table_name --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_table_schema --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_primaries --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_index_list --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_trigger_names --table-name 'users' --config 'D:\config\databases.json'
```

## 存在性检查

```powershell
DatabaseMcpServer tool is_any_table --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool is_any_column --table-name 'users' --column-name 'email' --config 'D:\config\databases.json'
DatabaseMcpServer tool is_any_constraint --constraint-name 'PK_users' --config 'D:\config\databases.json'
DatabaseMcpServer tool is_any_table_remark --table-name 'users' --config 'D:\config\databases.json'
```

## 查询

```powershell
DatabaseMcpServer tool sql_query --sql 'select top 10 * from users' --config 'D:\config\databases.json'
DatabaseMcpServer tool sql_query_single --sql 'select top 1 * from users order by id desc' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_scalar --sql 'select count(*) from users' --config 'D:\config\databases.json'
DatabaseMcpServer tool sql_query_with_in_parameter --sql 'select * from users where id in (@ids)' --in-parameter-name 'ids' --in-values '[1,2,3]' --config 'D:\config\databases.json'
```

## 写入 / DML

Always add `--yes`.

```powershell
DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where id=1' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool batch_execute_commands --commands '["update users set status=''active'' where id=1","update users set status=''inactive'' where id=2"]' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_monthly_report' --parameters '{"year":2025,"month":11}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool call_stored_procedure_with_output --procedure-name 'sp_user_statistics' --input-parameters '{"UserId":1001}' --output-parameters '["TotalOrders"]' --yes --config 'D:\config\databases.json'
```

## SQL Server `GO`

Always add `--yes`.

```powershell
DatabaseMcpServer tool execute_command_with_go --sql \"UPDATE users SET status='active' WHERE id=1`nGO`nUPDATE users SET status='inactive' WHERE id=2\" --yes --config 'D:\config\databases.json'
```

## 架构变更

Always add `--yes`.

```powershell
DatabaseMcpServer tool backup_table --old-table-name 'users' --new-table-name 'users_backup' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool rename_table --old-table-name 'users_tmp' --new-table-name 'users_archive' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool truncate_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":20,"IsNullable":true}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool update_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":30,"IsNullable":true}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool rename_column --table-name 'users' --old-column-name 'mobile' --new-column-name 'phone' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_column --table-name 'users' --column-name 'mobile' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_primary_key --table-name 'users' --column-name 'id' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_constraint --table-name 'users' --constraint-name 'PK_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_email' --column-name 'email' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_default_value --table-name 'users' --column-name 'status' --default-value '''active''' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_table_remark --table-name 'users' --description '用户表' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool delete_table_remark --table-name 'users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_column_remark --table-name 'users' --column-name 'email' --description '邮箱地址' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool delete_column_remark --table-name 'users' --column-name 'email' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_view --view-name 'v_active_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_func --function-name 'fn_calc_score' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_proc --procedure-name 'sp_cleanup_logs' --yes --config 'D:\config\databases.json'
```

## 导出

```powershell
DatabaseMcpServer tool export_query_to_excel --sql 'select * from users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'
DatabaseMcpServer tool export_table_to_excel --table-name 'users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'
DatabaseMcpServer tool export_multiple_queries_to_excel --queries-json '{"users":"select * from users","roles":"select * from roles"}' --file-path 'D:\exports\multi.xlsx' --return-format 'path' --config 'D:\config\databases.json'
```

## 文档生成

```powershell
DatabaseMcpServer tool generate_database_documentation --format 'markdown' --return-mode 'path' --file-path 'D:\exports\db-doc.md' --config 'D:\config\databases.json'
```
