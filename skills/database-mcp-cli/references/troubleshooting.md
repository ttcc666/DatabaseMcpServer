# DatabaseMcpServer CLI Troubleshooting

Use this file as a symptom-to-diagnosis matrix when CLI behavior is unclear.

## Quick Navigation

- `--yes` and destructive confirmation
- option parsing and unknown command failures
- config resolution failures
- stdout/stderr and JSON parsing issues
- SQL Server quoting and encryption issues
- `success: false` vs CLI usage failure
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
- `--queries-json`
- `--in-values`
- `--column-info`

Fix:

- wrap the whole JSON payload in single quotes
- pass one JSON object or array as one argument

Examples:

```powershell
--parameters '{"age":18}'
--in-values '[1,2,3]'
--queries-json '{"users":"select * from users","roles":"select * from roles"}'
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

Fix:

1. report the exact error first
2. do not silently weaken security
3. only if the user explicitly allows diagnosis, try a one-off diagnostic config:

```text
Encrypt=False;TrustServerCertificate=True
```

Important:

- this is diagnostic fallback only
- it is not equivalent to `Encrypt=True`

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

## 10. Large CLI verification runs

When testing many tools:

- start with read-only commands
- create isolated temporary objects for write and DDL coverage
- record command, exit code, stdout, stderr, and parsed success for each invocation
- keep cleanup status explicit

Recommended object prefix:

```text
cli_<yyyyMMdd_HHmmss>_<shortid>
```

Prefer `scripts/verify-cli-tools.ps1` when broad coverage is required.
