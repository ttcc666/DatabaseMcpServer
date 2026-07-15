# DatabaseMcpServer CLI Troubleshooting

Use this file as a symptom-to-diagnosis matrix when CLI behavior is unclear.

## Quick Navigation

- `--yes` and destructive confirmation
- option parsing and unknown command failures
- invalid `-web --port` usage
- config resolution failures
- stdout/stderr and JSON parsing issues
- SQL Server quoting and encryption issues
- `success: false` vs CLI usage failure
- current connection vs default connection confusion
- `reload_database_config` preserving current connection
- batch item failures and maximum query count
- source tree vs installed global-tool mismatches
- broad verification guidance

## Quick Triage

| Symptom | Meaning | Next action |
| --- | --- | --- |
| exit code `0` + parseable JSON | invocation succeeded | inspect payload semantics |
| exit code `1` | tool returned structured failure | treat as backend/tool failure first |
| exit code `2` | CLI usage failure | inspect parameters, config resolution, help output |
| help text on `stderr` | often expected for help/list commands | do not misclassify as failure |
| command result on `stdout` | normal | parse JSON before judging |

## 1. Missing `--yes`

Symptom:

```text
tool 'execute_command' 需要显式确认。请追加 '--yes'。
```

Or for config removal:

```text
命令 'remove' 需要显式确认。请追加 '--yes'。
```

Meaning:

- CLI usage error
- not a database connection failure
- exit code should be `2`

Fix:

- add `--yes`
- keep diagnosis in the CLI layer, not the database layer

Related: SKILL.md "Use `--yes` Correctly" lists every command that requires it.

## 2. Missing or invalid options

Typical symptoms:

```text
缺少必填选项 '--table-name'
未知选项: '--table'
选项 '--config' 缺少值。
无法识别的参数: 'users'
选项 '--max-retries' 需要 int 值。
选项 '--test-connections' 需要 bool 值。
```

Meaning:

- parameter binding or parsing failure inside CLI
- exit code should be `2`

Fix:

- run `DatabaseMcpServer tool help <tool_name>` or `DatabaseMcpServer config help <subcommand>`
- use kebab-case option names such as `--table-name`, not ad-hoc variants
- keep JSON, bool, and integer arguments in the expected format

## 3. Unknown command or tool name

Typical symptoms:

```text
未知命令: 'tools'。可用顶层命令: tool, init, config
未知 tool: 'get-table-schema'。
未知 config 命令: 'lists'。
```

Meaning:

- wrong command family or wrong naming form
- tool names are `snake_case`
- option names are `kebab-case`

Fix:

- switch to `tool`, `init`, or `config`
- use `DatabaseMcpServer tool list`
- use `DatabaseMcpServer tool help <tool_name>`
- if the CLI prints “最接近的命令”, prefer one of those suggestions

## 3.1 Invalid `-web --port`

Typical symptom:

```text
选项 '--port' 必须在 0-65535 之间。命令: -web
```

Meaning:

- CLI usage failure
- exit code should be `2`
- the Web host did not start yet

Fix:

- use an integer between `0` and `65535`
- omit `--port` entirely if you want the server to auto-pick a free localhost port

## 4. Config file not found

Typical symptom:

```text
未找到数据库配置文件。CLI 查找顺序: --config -> ./databases.json -> ./local-databases.json -> DB_CONFIG_PATH -> %USERPROFILE%/.database-mcp/databases.json
```

Or:

```text
配置文件不存在: D:\path\to\databases.json
```

Meaning:

- the CLI could not resolve a usable config file
- exit code should be `2`

Fix:

- prefer explicit `--config 'D:\config\databases.json'`
- or place a config in `./databases.json` or `./local-databases.json`
- or set `DB_CONFIG_PATH`
- remember `init` and `config` default to `%USERPROFILE%/.database-mcp/databases.json`
- remember `-web` behaves differently from `tool`: if no config exists yet, it still starts and treats `%USERPROFILE%/.database-mcp/databases.json` as the writable target so the user can initialize it from the page

## 5. `stdout` is not pure JSON

Normal behavior:

- successful `tool`, `config`, and `init` invocations write JSON payloads to `stdout`

If extra text appears:

- suspect a library or driver writing directly to `Console.Out`
- do not assume the CLI contract is broken until you capture exact `stdout` and `stderr`

Fix:

- record the full command, `stdout`, and `stderr`
- separate help output on `stderr` from stray runtime output
- if JSON is mixed with other text, note that as a CLI-contract regression

## 6. JSON argument parsing failures

Typical symptom:

```text
选项 '--parameters' 需要有效 JSON。
```

Common triggers:

- `--parameters`
- `--input-parameters`
- `--output-parameters`
- `--commands`
- `--parameters-array`
- `--queries`
- `--in-values`
- `--column-info`

Fix:

- wrap the whole JSON payload in single quotes
- pass one JSON object or array as one argument

Examples:

```powershell
--parameters '{"age":18}'
--in-values '[1,2,3]'
--queries '["select count(*) from users","select count(*) from roles"]'
```

## 7. SQL Server `add_default_value` string literal confusion

Wrong:

```powershell
--default-value 'active'
```

Correct:

```powershell
--default-value '''active'''
```

Meaning:

- the command expects a SQL literal, not plain text
- SQL Server needs `'active'`, while PowerShell single-quote escaping requires `'''active'''`

## 8. SQL Server `Encrypt=True` failure

Typical symptom:

```text
您试图连接的 SQL Server 实例要求加密，但是此计算机不支持
```

Meaning:

- driver or TLS capability problem
- not a tool-name or CLI-parameter issue

Fix, ordered safest first:

1. Enable/update TLS 1.2 support and install the server CA chain on the client.
2. On a trusted network, keep encryption but skip identity validation only with explicit consent: `Encrypt=True;TrustServerCertificate=True`.
3. Use `Encrypt=False` only as a temporary diagnostic fallback with explicit consent.

Never silently edit the user's real config. Use a temp config for diagnostic downgrades and state exactly what security property changes.

## 9. `success: false` vs CLI failure

If the payload is valid JSON and includes:

```json
{ "success": false }
```

Meaning:

- the command reached the tool layer
- exit code should usually be `1`
- classify this as tool/backend/database failure unless proven otherwise

Examples:

- object does not exist
- backend capability not supported
- permission denied
- database-specific limitation

## 10. Current connection vs default connection

Typical symptom:

```text
# user ran:
DatabaseMcpServer tool switch_database --database-name 'reporting' --config 'D:\config\databases.json'

# then they opened databases.json, see isDefault still on the old connection, and ask:
#   "switch_database didn't work? my default connection didn't change."
```

Meaning:

- `tool switch_database` writes the current connection to `%USERPROFILE%/.database-mcp/cli-state.json` (keyed by the resolved config path). It does **not** rewrite `databases.json`.
- `config use --name X` is the command that changes `isDefault` inside the config file.
- Both are working as designed — they target different state layers.

Fix:

- If the user wants the CLI to remember a connection across `tool` calls without editing the config file, `switch_database` already did the right thing. Prove it with `tool get_current_database`.
- If the user wants `databases.json` to show the change, use `config use --name X`.
- To reset CLI-only state, delete `%USERPROFILE%/.database-mcp/cli-state.json` or run `switch_database` back to the intended connection name.

Sanity check pattern:

```powershell
DatabaseMcpServer tool get_current_database --config 'D:\config\databases.json'
DatabaseMcpServer tool list_databases --config 'D:\config\databases.json'
DatabaseMcpServer config show --name '<expected-name>' --config 'D:\config\databases.json'
```

Related: SKILL.md "Mental Model" explains the two-layer state design; `references/cli.md` §3.6 has the full specification.

## 11. `reload_database_config` seems to ignore a connection change

Typical symptom:

```text
# user edited databases.json, removed or renamed the current connection, ran:
DatabaseMcpServer tool reload_database_config --config 'D:\config\databases.json'
# then expects the CLI to fall back to the new isDefault, but it doesn't until the saved name becomes invalid.
```

Meaning:

- `reload_database_config` intentionally preserves the current connection when the saved name still exists in the reloaded file.
- It only falls back to `isDefault` if the saved name was removed.

Fix:

- If the user wants the reload to also drop the CLI-only current connection, follow up with `tool switch_database --database-name '<new-target>'`, or delete `cli-state.json`.

Related: SKILL.md "Mental Model" explains why the current connection is preserved across reloads.

## 12. Batch result contains item failures

Typical payload:

```json
{
  "success": true,
  "totalQueries": 3,
  "successfulQueries": 2,
  "failedQueries": 1,
  "results": [
    { "success": true, "queryIndex": 0 },
    { "success": false, "queryIndex": 1, "error": "检测到危险操作" }
  ]
}
```

Meaning:

- The batch invocation itself succeeded, but one item failed.
- `batch_sql_query` accepts 1-5 read-only SQL strings and does not require `--yes`.
- `batch_execute_commands` requires `--yes`, is not transactional, and can persist earlier successful writes when a later item fails.

Fix:

- inspect every `results[]` item instead of trusting only top-level `success`
- for `batch_sql_query`, reduce input to at most five items and keep every SQL read-only
- if atomic writes are required, do not claim `batch_execute_commands` provides rollback

## 13. New source tool is missing from the global CLI

Typical symptom:

```text
未知 tool: 'batch_sql_query'
```

Meaning:

- The repository source may contain the tool while the installed global package is older.
- This is not evidence that source registration is broken.

Diagnosis:

```powershell
dotnet tool list --global | Select-String databasemcpserver
dotnet run --project 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -f 'net9.0' -- tool help batch_sql_query
DatabaseMcpServer tool help batch_sql_query
```

Report which executable produced each result. Pack and install a newly versioned local package, or update the global tool, before retesting.

## 14. Large CLI verification runs

When testing many tools:

- start with read-only commands
- create isolated temporary objects for write and DDL coverage
- record command, exit code, stdout, stderr, and parsed success for each invocation
- keep cleanup status explicit

Recommended object prefix:

```text
cli_<yyyyMMdd_HHmmss>_<shortid>
```
