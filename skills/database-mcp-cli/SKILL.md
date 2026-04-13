---
name: database-mcp-cli
description: "Use when the user wants to run, configure, script, explain, or troubleshoot DatabaseMcpServer in CLI / 命令行模式 instead of MCP stdio client integration. Trigger for `DatabaseMcpServer tool ...`, `DatabaseMcpServer config ...`, `DatabaseMcpServer init`, `DatabaseMcpServer --help`, `tool list`, `tool help`, `config doctor`, `config import`, `config export`, `switch_database`, or `reload_database_config`. Also trigger on requests involving `databases.json`, `local-databases.json`, `DB_CONFIG_PATH`, temporary config generation from a connection string, CLI command examples/docs, stdout/stderr/exit code or 返回码 verification, PowerShell quoting, script integration, `--config`, `--yes`, config repair, or CLI smoke tests. Chinese trigger examples: “命令行模式”, “命令行调用”, “CLI 测试”, “生成 databases.json”, “切换数据库”, “重载配置”, “检查退出码”, “看 stdout/stderr”, “脚本集成”, “PowerShell 转义”."
---

# DatabaseMcpServer CLI

Drive `DatabaseMcpServer` from the shell safely and repeatably.

## Start Here

- Confirm the user wants CLI behavior, not MCP stdio transport.
- Prefer `DatabaseMcpServer` on PATH.
- Inside this repo, prefer a built executable under `bin/<Configuration>/<Framework>/DatabaseMcpServer.exe`; use `net9.0` unless the task says otherwise.
- Build before testing if the binary may be stale:

```powershell
dotnet build 'DatabaseMcpServer.csproj' --framework 'net9.0'
```

- Avoid `dotnet run -- tool ...` for large loops; it is slower and more likely to hit file locks.

## Pick the Command Family

- Use `DatabaseMcpServer tool ...` to invoke MCP tools directly.
- Use `DatabaseMcpServer config ...` or `DatabaseMcpServer init` to create, inspect, validate, import, export, or repair config files.
- Use `DatabaseMcpServer tool list`, `DatabaseMcpServer tool help <tool_name>`, or `DatabaseMcpServer config <subcommand> --help` when parameter names are unclear.

## Handle Config Deliberately

- Prefer explicit `--config <path>` when the user already has a target config file.
- If the user only provides a connection string, create a temporary one-entry `databases.json` under `%TEMP%`.
- Keep secrets out of repo-tracked files unless the user explicitly asks to persist them.
- Delete temporary configs after testing if they contain credentials.
- For repeatable local usage, suggest moving the final config to a stable path after validation.

Use this minimal shape for ad-hoc config generation:

```json
{
  "databases": [
    {
      "name": "temp-db",
      "connectionString": "<user-connection-string>",
      "dbType": "<MySql|PostgreSQL|SqlServer|Sqlite|...>",
      "description": "temporary cli test",
      "isDefault": true
    }
  ]
}
```

Config resolution matters:

- `tool` without `--config` searches in this order:
  1. `./databases.json`
  2. `./local-databases.json`
  3. `DB_CONFIG_PATH`
  4. `%USERPROFILE%/.database-mcp/databases.json`
- `init` and `config` default to `%USERPROFILE%/.database-mcp/databases.json` unless `--config` is supplied.

## Follow This Workflow

### 1. Read-only first

Start with:

1. `validate_configuration`
2. `test_connection`
3. `get_database_config`
4. `list_databases` or `get_current_database`
5. `health_check` when broader connectivity matters

### 2. Inspect schema before SQL

Before querying or changing objects:

1. `is_any_table` or `get_table_info_list`
2. `get_table_schema` or `get_column_infos_by_table_name`
3. `get_primaries`, `get_index_list`, or `get_trigger_names` when relevant

Do not guess table names, column names, or constraint names.

### 3. Isolate write and DDL coverage

- Never test write or schema tools on business tables unless the user explicitly asks.
- Create uniquely prefixed temporary objects such as `cli_<yyyyMMdd_HHmmss>_<shortid>`.
- Clean up created objects before finishing.
- If the user has not approved write or high-risk behavior, stop at read-only validation or provide a plan only.

## Respect the CLI Contract

- Successful tool calls write their payload to `stdout`.
- Help text and CLI usage errors go to `stderr`.
- Exit code `0` means success.
- Exit code `1` means the tool ran and returned a structured failure, usually `success: false`.
- Exit code `2` means CLI usage failure, such as missing parameters, unknown options, or missing `--yes`.
- `get_database_config` can still be valid on exit code `0` even when there is no top-level `success` field; treat `exit code 0 + parseable JSON` as success.

Classify failures correctly:

- CLI issue: missing `--yes`, missing required option, unknown option, help text on `stderr`, malformed JSON argument, quoting bug.
- Backend issue: `success: false`, unsupported database capability, missing database object, driver/TLS problem, permission problem.

## Use `--yes` Correctly

Add `--yes` for write or high-risk commands, including:

- DML and batch execution
- Stored procedure calls
- Table, column, index, constraint, remark, and default-value changes
- Drop, truncate, rename, backup, and similar destructive operations

If the CLI says `需要显式确认。请追加 '--yes'。`, treat it as a CLI usage error and fix the command. Do not misclassify it as a database failure.

## Quote PowerShell Arguments Carefully

- Prefer single quotes around SQL and JSON arguments.
- Keep each JSON object or array as one argument.
- For `batch_execute_commands`, pass one JSON array string.
- For `execute_command_with_go`, pass one SQL argument containing embedded newlines and `GO`.
- For SQL Server `add_default_value`, pass the SQL literal itself, not bare text:

```powershell
--default-value '''active'''
```

- For scripted automation or long verification loops, prefer `ProcessStartInfo.ArgumentList` over composing a single shell string.

## Diagnose Without Weakening Security

If SQL Server fails with `Encrypt=True` and the error says encryption is required but unsupported on the machine:

- Report the exact error first.
- Do not silently weaken the connection string.
- Only if the user explicitly wants diagnosis, try a one-off diagnostic config such as `Encrypt=False;TrustServerCertificate=True`.
- Label that fallback as diagnostic only, not equivalent security posture.

## Reuse Existing Verification Assets

- Read `references/commands.md` for exact command names and common PowerShell examples.
- Read `references/troubleshooting.md` for `--yes`, stdout/stderr, quoting, SQL Server encryption, and backend-vs-CLI diagnosis.
- Read `Doc/cli.md` when the task involves `config` or `init` behavior, config search order, or the full CLI contract.
- Prefer `scripts/verify-cli-tools.ps1` when the user wants broad CLI coverage against a real database instead of inventing a new exhaustive loop from scratch.

## Report Results Clearly

When you verify or troubleshoot CLI behavior, report:

- executable path and config source
- exact command or generated argument list
- exit code, stdout, stderr, and parsed JSON outcome
- whether the failure belongs to the CLI layer or the database/driver layer
- cleanup status for temp configs and temporary database objects
