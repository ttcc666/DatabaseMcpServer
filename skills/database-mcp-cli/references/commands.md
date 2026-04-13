# DatabaseMcpServer CLI Commands

Use this file as a high-frequency command sheet, not as the full CLI spec.

For exhaustive `config` and `init` behavior, read `Doc/cli.md`.

## Quick Navigation

- Root and help commands
- Config bootstrap and repair
- Safe read-only tool flow
- High-risk write and schema examples
- Export and documentation examples
- Broad verification workflow

## Root and Help Commands

```powershell
DatabaseMcpServer --help
DatabaseMcpServer tool list
DatabaseMcpServer tool help get_table_schema
DatabaseMcpServer tool get_table_schema --help
DatabaseMcpServer config help add
```

Notes:

- Root help and help text are written to `stderr`.
- `tool list` also writes to `stderr` even on success.
- Actual command results are written to `stdout` as JSON.

## Config Bootstrap and Repair

### Initialize or choose a config file

```powershell
DatabaseMcpServer init
DatabaseMcpServer init --config 'D:\config\databases.json'
DatabaseMcpServer init --config 'D:\config\databases.json' --force
```

### Create and maintain named connections

```powershell
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'Sqlite'

DatabaseMcpServer config add `
  --name 'sqlite-local' `
  --db-type 'Sqlite' `
  --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' `
  --description 'local sqlite' `
  --set-default

DatabaseMcpServer config list
DatabaseMcpServer config show --name 'sqlite-local'
DatabaseMcpServer config test --name 'sqlite-local'
DatabaseMcpServer config validate
DatabaseMcpServer config doctor
DatabaseMcpServer config export --output '.\backup-databases.json'
DatabaseMcpServer config import --input '.\backup-databases.json' --config 'D:\config\databases.json' --force
DatabaseMcpServer config remove --name 'sqlite-local' --yes
```

Config resolution reminder:

- `tool` without `--config` searches `./databases.json`, then `./local-databases.json`, then `DB_CONFIG_PATH`, then `%USERPROFILE%/.database-mcp/databases.json`.
- `init` and `config` default to `%USERPROFILE%/.database-mcp/databases.json` unless `--config` is supplied.

## Safe Read-only Tool Flow

Run these in order when the user wants verification, troubleshooting, or discovery:

```powershell
DatabaseMcpServer tool validate_configuration --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection_by_name --database-name 'reporting' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_database_config --config 'D:\config\databases.json'
DatabaseMcpServer tool reload_database_config --config 'D:\config\databases.json'
DatabaseMcpServer tool list_databases --config 'D:\config\databases.json'
DatabaseMcpServer tool switch_database --database-name 'reporting' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_current_database --config 'D:\config\databases.json'
DatabaseMcpServer tool health_check --config 'D:\config\databases.json'
```

Before querying or mutating a table, inspect schema first:

```powershell
DatabaseMcpServer tool is_any_table --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_table_info_list --config 'D:\config\databases.json'
DatabaseMcpServer tool get_column_infos_by_table_name --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_table_schema --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_primaries --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_index_list --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_trigger_names --table-name 'users' --config 'D:\config\databases.json'
```

## Query Patterns

```powershell
DatabaseMcpServer tool sql_query --sql 'select top 10 * from users' --config 'D:\config\databases.json'
DatabaseMcpServer tool sql_query_single --sql 'select top 1 * from users order by id desc' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_scalar --sql 'select count(*) from users' --config 'D:\config\databases.json'
DatabaseMcpServer tool sql_query_with_in_parameter --sql 'select * from users where id in (@ids)' --in-parameter-name 'ids' --in-values '[1,2,3]' --config 'D:\config\databases.json'
```

Use single quotes around SQL and JSON arguments in PowerShell.

## High-risk Write and Schema Examples

Always add `--yes`.

### DML and procedure calls

```powershell
DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where id=1' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool batch_execute_commands --commands '["update users set status=''active'' where id=1","update users set status=''inactive'' where id=2"]' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_monthly_report' --parameters '{"year":2025,"month":11}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool call_stored_procedure_with_output --procedure-name 'sp_user_statistics' --input-parameters '{"UserId":1001}' --output-parameters '["TotalOrders"]' --yes --config 'D:\config\databases.json'
```

### SQL Server `GO`

```powershell
DatabaseMcpServer tool execute_command_with_go --sql "UPDATE users SET status='active' WHERE id=1`nGO`nUPDATE users SET status='inactive' WHERE id=2" --yes --config 'D:\config\databases.json'
```

### Common schema changes

```powershell
DatabaseMcpServer tool backup_table --old-table-name 'users' --new-table-name 'users_backup' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool rename_table --old-table-name 'users_tmp' --new-table-name 'users_archive' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool truncate_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":20,"IsNullable":true}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool update_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":30,"IsNullable":true}' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool rename_column --table-name 'users' --old-column-name 'mobile' --new-column-name 'phone' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_column --table-name 'users' --column-name 'mobile' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_email' --column-name 'email' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_default_value --table-name 'users' --column-name 'status' --default-value '''active''' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_table_remark --table-name 'users' --description '用户表' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool add_column_remark --table-name 'users' --column-name 'email' --description '邮箱地址' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_view --view-name 'v_active_users' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_func --function-name 'fn_calc_score' --yes --config 'D:\config\databases.json'
DatabaseMcpServer tool drop_proc --procedure-name 'sp_cleanup_logs' --yes --config 'D:\config\databases.json'
```

## Export and Documentation Examples

```powershell
DatabaseMcpServer tool export_query_to_excel --sql 'select * from users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'
DatabaseMcpServer tool export_table_to_excel --table-name 'users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'
DatabaseMcpServer tool export_multiple_queries_to_excel --queries-json '{"users":"select * from users","roles":"select * from roles"}' --file-path 'D:\exports\multi.xlsx' --return-format 'path' --config 'D:\config\databases.json'
DatabaseMcpServer tool generate_database_documentation --format 'markdown' --return-mode 'path' --file-path 'D:\exports\db-doc.md' --config 'D:\config\databases.json'
```

## Broad Verification Workflow

When the user asks to test many tools or “test all CLI commands”:

1. Build the executable once.
2. Create a temporary config and isolated database objects.
3. Run read-only checks first.
4. Run high-risk tools only against disposable objects.
5. Capture command, exit code, stdout, stderr, and parsed JSON status for each invocation.
6. Clean up temp configs and temporary database objects.

Prefer reusing `scripts/verify-cli-tools.ps1` instead of rebuilding this loop manually.
