# DatabaseMcpServer CLI — PowerShell Gotchas & Quick Reference

This file is **not** a command catalog. The full catalog lives in `references/cli.md` §4 (config management), §6 (PowerShell quoting), §7 (high-risk commands), and §8 (tool catalog). Read this file for the PowerShell-specific patterns and CLI quirks that tripped real users up.

## Root and Help

```powershell
DatabaseMcpServer --help
DatabaseMcpServer -web --help
DatabaseMcpServer tool list
DatabaseMcpServer tool help get_table_schema
DatabaseMcpServer tool get_table_schema --help
DatabaseMcpServer config help add
```

Important: both root help and `tool list` write to **stderr**, not stdout. Don't misclassify a working help invocation as a failure just because stdout is empty.

## `-web` quick usage

```powershell
DatabaseMcpServer -web
DatabaseMcpServer -web --config 'D:\config\databases.json'
DatabaseMcpServer -web --config 'D:\config\databases.json' --port 5129 --no-browser
```

Quick notes:

- `-web` starts a localhost-only browser UI; it is not a remote admin endpoint.
- `--no-browser` is useful in terminals / CI / remote sessions where auto-open would be noisy or impossible.
- Invalid ports are now rejected in the CLI layer. Example symptom:

```text
选项 '--port' 必须在 0-65535 之间。命令: -web
```

## Config file vs CLI current connection

The single most common source of confusion, surfaced here so Claude can route the user correctly without a full file-dive.

| Command | Changes | Persists where |
| --- | --- | --- |
| `config use --name X` | `databases.json` `isDefault` | The config file (durable, visible, shared) |
| `tool switch_database --database-name X` | CLI current-connection pointer | `%USERPROFILE%/.database-mcp/cli-state.json`, keyed by the resolved config path |

`tool` calls pick the CLI current connection first; they only fall back to `isDefault` when cli-state is absent or points to a removed name. Sanity check:

```powershell
DatabaseMcpServer tool get_current_database --config 'D:\config\databases.json'
DatabaseMcpServer config show --name '<expected>' --config 'D:\config\databases.json'
```

## PowerShell quoting gotchas

The tool catalog in `cli.md` shows vanilla examples; these are the sharp edges.

### 1. Single quotes around SQL and JSON — always

```powershell
--sql 'select * from users where status=''active'''
--parameters '{"age":18,"city":"北京"}'
--in-values '[1,2,3]'
--queries '["select count(*) from users","select count(*) from roles"]'
--commands '["update users set status=''active'' where id=1","update users set status=''inactive'' where id=2"]'
```

Why: single quotes disable PowerShell variable expansion, so `$` and backtick inside SQL won't be interpreted. Within a single-quoted string, a literal single quote is written as `''` (two in a row).

### 2. SQL Server `add_default_value` needs a SQL literal, not text

```powershell
# Wrong — passes the bare word 'active' as default, which is not valid SQL
--default-value 'active'

# Right — the tool expects SQL syntax, and SQL literal is 'active', which in PowerShell single-quoted form is '''active'''
--default-value '''active'''
```

### 3. `execute_command_with_go` needs embedded newlines and `GO` in one argument

```powershell
$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@

DatabaseMcpServer tool execute_command_with_go `
  --sql $sql `
  --yes `
  --config 'D:\config\databases.json'
```

Use a here-string so the CLI receives one argv item containing real newlines and standalone `GO` lines.

### 4. Batch query vs batch command

```powershell
# Read-only, 1-5 queries, no --yes
DatabaseMcpServer tool batch_sql_query `
  --queries '["select count(*) from users","select count(*) from roles"]' `
  --config 'D:\config\databases.json'

# Writes, per-command parameters, requires --yes
DatabaseMcpServer tool batch_execute_commands `
  --commands '["update users set status=@status where id=@id","delete from sessions where user_id=@id"]' `
  --parameters-array '[{"status":"active","id":1},{"id":1}]' `
  --yes `
  --config 'D:\config\databases.json'
```

Both tools continue after an item-level failure and return per-item status in `results[]`. `batch_execute_commands` is not transactional, so successful earlier writes remain committed.

### 5. Scripted automation — skip the shell entirely

```csharp
var psi = new ProcessStartInfo("DatabaseMcpServer") {
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};
psi.ArgumentList.Add("tool");
psi.ArgumentList.Add("execute_command");
psi.ArgumentList.Add("--sql");
psi.ArgumentList.Add(sql); // literal, no escaping
psi.ArgumentList.Add("--yes");
```

`ArgumentList` hands each argv entry to the OS verbatim — no quoting bugs possible.

## Broad verification workflow

When the user asks to "test all CLI commands" or run a smoke test across many tools:

1. Identify whether the run targets the installed global tool or current source. Confirm the installed version with `dotnet tool list --global` (the CLI binary itself has no `--version` flag — it returns exit `2` on that argument); run `dotnet tool update --global DatabaseMcpServer` if it's stale.
2. Generate a temporary config in `%TEMP%` with a throwaway database connection — never aim the smoke test at a business database.
3. Create isolated temporary objects with prefix `cli_<yyyyMMdd_HHmmss>_<shortid>`.
4. Run read-only tools first (`validate_configuration`, `test_connection`, `list_databases`, `get_table_info_list`). Any failure here means the rest won't tell you anything useful.
5. Run write and DDL tools only against the temporary objects. Every write-class command needs `--yes`.
6. Capture for each invocation: executable path, full argv, exit code, stdout, stderr, parsed `success` field.
7. Clean up: drop temp objects, delete temp config (especially if it held credentials).
