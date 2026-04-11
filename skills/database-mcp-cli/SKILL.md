---
name: database-mcp-cli
description: Use this skill whenever the user wants to operate DatabaseMcpServer through its CLI instead of MCP transport. Trigger on requests to run `DatabaseMcpServer tool ...`, troubleshoot CLI-only behavior, generate temporary `databases.json` files from a connection string, verify CLI stdout/stderr/exit codes, build CLI smoke tests, or test all tools against a real database. Also use it when the user mentions `--config`, `--yes`, PowerShell quoting, CLI command docs, or wants examples for any DatabaseMcpServer tool command.
---

# DatabaseMcpServer CLI

Use this skill to drive `DatabaseMcpServer` from the command line in a safe, repeatable way.

## Compatibility

- Best on Windows PowerShell
- Requires shell access
- Requires `DatabaseMcpServer` executable on PATH, or a local build of the repo

## Goal

- Run `DatabaseMcpServer tool ...` correctly
- Avoid quoting mistakes in PowerShell
- Distinguish CLI failures from database/backend failures
- Test real databases without touching business objects when write/DDL coverage is needed

## Pick the executable first

- Prefer `DatabaseMcpServer` if it is on PATH.
- If working inside the source repo, prefer the built executable under `bin/Debug/net9.0/DatabaseMcpServer.exe`.
- If the binary may be stale, build first:

```powershell
dotnet build 'DatabaseMcpServer.csproj' --framework 'net9.0'
```

- Avoid `dotnet run -- tool ...` for large test loops. It is slower and more likely to hit build/output locks.

## Handle config safely

- If the user already has a config file, use `--config <path>`.
- If the user only gives a connection string, create a temporary `databases.json` under `%TEMP%`.
- Put secrets only in the temporary config, never into repo-tracked files unless the user explicitly asks.
- Delete temporary configs after finishing if they contain credentials.
- If the user wants repeatable local use, suggest moving the final config to a stable path after testing.

Use this one-entry shape for ad-hoc testing:

```json
{
  "databases": [
    {
      "name": "temp-db",
      "connectionString": "<user-connection-string>",
      "dbType": "<MySql|PostgreSQL|SqlServer|...>",
      "description": "temporary cli test",
      "isDefault": true
    }
  ]
}
```

## Follow this workflow

### Read-only first

Always start with:

1. `validate_configuration`
2. `test_connection`
3. `get_database_config`
4. `list_databases` / `get_current_database`
5. `health_check` when broader connectivity matters

### Schema before SQL

Before querying or changing a table:

1. `is_any_table` or `get_table_info_list`
2. `get_table_schema` or `get_column_infos_by_table_name`
3. `get_primaries` / `get_index_list` / `get_trigger_names` if relevant

Do not guess table names, column names, or constraint names.

### Isolate write/DDL tests

If the user asks to test write or schema tools against a real database:

- Never touch existing business tables
- Create uniquely prefixed temporary objects
- Use a prefix like `cli_<yyyyMMdd_HHmmss>_<shortid>`
- Clean up at the end

If the user has not approved write/DDL behavior, stop at read-only validation or give a plan only.

## Remember the CLI contract

- `stdout` carries tool results
- `stderr` carries help text or CLI usage errors
- Successful tool calls should ideally return only JSON on `stdout`
- Exit codes:
  - `0` success
  - `1` tool executed and returned structured failure (`success: false`)
  - `2` CLI usage error

Important: `get_database_config` may return valid JSON without a top-level `success` field. Treat `exit code 0 + parseable JSON` as success.

## Use `--yes` correctly

These command families require `--yes`:

- DML and stored procedures
- Batch commands
- Table/column/index/constraint changes
- Remarks/default values
- Drop/truncate/rename/backup operations

If the CLI says `需要显式确认。请追加 '--yes'。`, fix the command. Do not misclassify it as a database failure.

## PowerShell quoting rules

- Prefer single quotes around SQL and JSON arguments
- Keep JSON objects/arrays as one argument
- For SQL Server string default values with `add_default_value`, pass the SQL literal itself, including quotes. Example:

```powershell
--default-value '''active'''
```

- For `batch_execute_commands`, pass one JSON array string
- For `execute_command_with_go`, pass one argument containing newlines and `GO`
- If quoting becomes fragile, switch to `ProcessStartInfo.ArgumentList` instead of a single shell string

## SQL Server encryption note

If SQL Server connection with `Encrypt=True` fails and the message says encryption is required but unsupported on this machine:

- Report the exact error first
- Do not silently weaken security
- If the user wants diagnosis, try a one-off config with `Encrypt=False;TrustServerCertificate=True`
- Clearly label that as a diagnostic fallback, not an equivalent security posture

## Full verification pattern

For comprehensive CLI verification:

1. Create temp config in `%TEMP%`
2. Run the read-only connection/config checks
3. Bootstrap isolated temporary objects if write/DDL tools need coverage
4. Execute CLI tools against those isolated objects
5. Capture per tool:
   - command
   - exit code
   - parsed JSON success
   - error message
   - stdout/stderr
6. Clean up temporary objects
7. Summarize:
   - CLI issues
   - backend/driver issues
   - cleanup status

## Report structure

Use this structure by default:

```md
## 上下文
- CLI 可执行路径:
- 配置来源:
- 目标数据库:

## 执行结果
- 通过:
- 失败:
- 关键错误:

## 结论
- CLI 层问题:
- 数据库/驱动层问题:
- 清理状态:
```

## References

- Read `references/commands.md` when you need exact command names and common examples.
- Read `references/troubleshooting.md` when the issue is quoting, `--yes`, stdout/stderr, SQL Server encryption, or backend-specific unsupported behavior.
