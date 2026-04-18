# DatabaseMcpServer CLI — PowerShell Gotchas & Quick Reference

This file is **not** a command catalog. The full catalog lives in `references/cli.md` §4 (config management), §6 (PowerShell quoting), §7 (high-risk commands), and §8 (tool catalog). Read this file for the PowerShell-specific patterns and CLI quirks that tripped real users up.

## Root and Help

```powershell
DatabaseMcpServer --help
DatabaseMcpServer tool list
DatabaseMcpServer tool help get_table_schema
DatabaseMcpServer tool get_table_schema --help
DatabaseMcpServer config help add
```

Important: both root help and `tool list` write to **stderr**, not stdout. Don't misclassify a working help invocation as a failure just because stdout is empty.

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
--queries-json '{"users":"select * from users","roles":"select * from roles"}'
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
DatabaseMcpServer tool execute_command_with_go `
  --sql "UPDATE users SET status='active' WHERE id=1`nGO`nUPDATE users SET status='inactive' WHERE id=2" `
  --yes --config 'D:\config\databases.json'
```

Double quotes here because we want `` `n `` to become a newline; inside SQL, ordinary single quotes are fine.

### 4. Scripted automation — skip the shell entirely

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

1. Confirm the installed version with `dotnet tool list --global` (the CLI binary itself has no `--version` flag — it returns exit `2` on that argument); run `dotnet tool update --global DatabaseMcpServer` if it's stale.
2. Generate a temporary config in `%TEMP%` with a throwaway database connection — never aim the smoke test at a business database.
3. Create isolated temporary objects with prefix `cli_<yyyyMMdd_HHmmss>_<shortid>`.
4. Run read-only tools first (`validate_configuration`, `test_connection`, `list_databases`, `get_table_info_list`). Any failure here means the rest won't tell you anything useful.
5. Run write and DDL tools only against the temporary objects. Every write-class command needs `--yes`.
6. Capture for each invocation: executable path, full argv, exit code, stdout, stderr, parsed `success` field.
7. Clean up: drop temp objects, delete temp config (especially if it held credentials).
