---
name: database-mcp-cli
description: "Run, configure, script, validate, or troubleshoot DatabaseMcpServer in CLI mode / 命令行模式 (not MCP stdio client setup). Use for `DatabaseMcpServer tool|config|init|-web`, config discovery and `DB_CONFIG_PATH`, `databases.json` vs `cli-state.json`, `switch_database` vs `config use`, `--config` / `--yes`, exit-code and stdout/stderr triage, PowerShell SQL/JSON quoting, source-vs-global-tool version mismatches, `batch_sql_query`, `batch_execute_commands`, browser config management, or reproducible CLI smoke tests. Chinese triggers include CLI 测试、切换数据库、当前连接没变、重载配置、PowerShell 转义、批量查询、批量执行、打开配置网页、退出码排查。"
---

# DatabaseMcpServer CLI

Operate DatabaseMcpServer from the shell safely, deterministically, and with evidence.

## Scope Gate

- Use this skill for direct CLI invocations: `tool`, `config`, `init`, or `-web`.
- Do not use CLI instructions to answer “how do I connect Claude/Cursor/Cline over MCP stdio?” Treat that as client integration.
- Identify which executable is under test:
  - Installed global tool: `DatabaseMcpServer`
  - Current repository source: `dotnet run --project 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -f 'net9.0' -- ...`
- Never assume the installed tool contains a source-tree change. If a new tool exists in source but global CLI says “unknown tool”, compare source help with `dotnet tool list --global`.

## Core Workflow

1. Discover exact syntax before guessing:

   ```powershell
   DatabaseMcpServer tool list
   DatabaseMcpServer tool help <tool_name>
   DatabaseMcpServer config help <subcommand>
   ```

2. Resolve the executable version and config path.
3. Establish the current connection with `get_current_database`; do not infer it from `isDefault`.
4. Run read-only checks before SQL or writes:
   `validate_configuration → test_connection → get_database_config → list_databases`.
5. Inspect schema before composing SQL:
   `is_any_table → get_table_schema/get_column_infos_by_table_name`.
6. Execute writes only within the user-authorized scope and with `--yes`.
7. Capture argv, exit code, stdout, stderr, classification, and cleanup.

## Version and Source Truth

- Check an installed version with:

  ```powershell
  dotnet tool list --global | Select-String databasemcpserver
  ```

- `DatabaseMcpServer --version` is unsupported and exits `2`; do not recommend it as a working check.
- When validating a repository change, prefer `dotnet run ... -- tool help <name>` or the freshly packed local tool. State explicitly whether evidence came from source or the installed global tool.
- If the global tool is stale, upgrade or install a newly versioned local package before diagnosing the feature as missing.

## Two State Layers

| State | Stored in | Changed by | Meaning |
| --- | --- | --- | --- |
| Default connection | `databases.json` `isDefault` | `config use` / `config set-default` | File-level default |
| CLI current connection | `%USERPROFILE%/.database-mcp/cli-state.json` | `tool switch_database` | Runtime selection, keyed by resolved config path |

Consequences:

- `switch_database` does not rewrite `databases.json`.
- Tool calls prefer cli-state and fall back to `isDefault` only when saved state is absent or invalid.
- `reload_database_config` preserves the current connection when that name still exists.
- Diagnose identity with `get_current_database` plus `list_databases`; use `config show` to inspect the file default.

## Config Resolution

- Prefer explicit `--config '<path>'` for deterministic scripts.
- `tool` and `-web` search:
  `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json`.
- `init` and `config` default to `%USERPROFILE%/.database-mcp/databases.json`.
- `-web` is localhost-only. With no existing config, it uses the user-level path as the writable target.
- For an ad-hoc connection string, create a one-entry config under `%TEMP%`; never modify the real user config unless asked. Delete temp credentials and database files after testing.

## Safety and Side Effects

- Start read-only. Do not test writes or DDL against business objects without explicit authorization.
- Use uniquely prefixed throwaway objects such as `cli_<yyyyMMdd_HHmmss>_<shortid>`.
- All DML, stored-procedure, batch-command, and schema mutation tools require `--yes`. Missing `--yes` is exit `2`; the database was not contacted.
- Never weaken TLS or certificate validation silently. Read `references/troubleshooting.md` §8 and present ranked options before changing a connection string.

### Batch Tools

| Tool | Contract | Confirmation |
| --- | --- | --- |
| `batch_sql_query` | 1–5 independent read-only SQL strings; sequential; one shared connection; per-item success/error; no per-query parameters | No `--yes` |
| `batch_execute_commands` | Write commands with optional per-command parameters; sequential; per-item success/error; **not transactional**, so partial success persists | Requires `--yes` |

For both tools, inspect every `results[]` item. Top-level `success: true` does not imply every item succeeded.

## CLI Result Contract

| Evidence | Classification | Next action |
| --- | --- | --- |
| Exit `0` + parseable JSON | Invocation succeeded | Inspect payload and per-item results |
| Exit `1` + structured `success:false` | Tool/backend/database failure | Diagnose object, permission, capability, or connection |
| Exit `2` + usage text | CLI-layer failure | Fix argv, JSON, config resolution, or `--yes` |
| Help/list text on stderr | Expected | Do not misclassify as failure |

`get_database_config` may return valid JSON without top-level `success`; exit `0` plus parseable JSON is authoritative.

## PowerShell Guardrails

- Wrap SQL and JSON values in single quotes.
- Keep each JSON object/array as one argv item.
- Escape a literal single quote inside a PowerShell single-quoted string as `''`.
- SQL Server `add_default_value` expects a SQL literal: `--default-value '''active'''`.
- Pass `execute_command_with_go` a single argument containing real newlines and standalone `GO` lines; prefer a PowerShell here-string.
- In automation, prefer `ProcessStartInfo.ArgumentList` over composing a shell command string.

Read `references/commands.md` for exact quoting and batch examples.

## Reference Routing

| Need | Read |
| --- | --- |
| End-to-end CLI lifecycle, config commands, exact tool catalog | `references/cli.md` |
| PowerShell quoting, GO scripts, JSON arrays, batch examples | `references/commands.md` |
| Exit codes, missing tool/version mismatch, config/state, TLS, batch partial failures | `references/troubleshooting.md` |

The repository's `Doc/cli.md` is canonical for `references/cli.md`. When changing CLI behavior, update both and verify removed tools do not remain in the skill reference.

## Reproducible Report

Report every CLI run with:

```text
Executable: <global tool | dotnet run from source>, version/commit: <value>
Config: <absolute path>, source: <--config | cwd | env | user default>
Current connection: <get_current_database result>
Command: <exact argv>
Exit code: <0|1|2>
Stdout: <JSON or empty>
Stderr: <text or empty>
Classification: <success | CLI usage | tool/backend>
Cleanup: <objects/files removed or none>
```
