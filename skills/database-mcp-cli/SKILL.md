---
name: database-mcp-cli
description: "Use when the user wants to run, configure, script, or troubleshoot DatabaseMcpServer in CLI mode / 命令行模式 (not MCP stdio client integration). Trigger for `DatabaseMcpServer tool ...`, `DatabaseMcpServer config ...`, `DatabaseMcpServer init`; questions about `databases.json`, `local-databases.json`, `cli-state.json`, `DB_CONFIG_PATH`, current-vs-default connection confusion, temp config generation from a connection string, `switch_database` vs `config use`, `--config`, `--yes`, exit codes, stdout/stderr triage, PowerShell quoting for SQL/JSON args, or `config doctor` output. Chinese triggers: 命令行模式, CLI 测试, 生成 databases.json, 切换数据库, 当前连接没变, 重载配置, 检查退出码, PowerShell 转义, 从连接串生成临时配置."
---

# DatabaseMcpServer CLI

Drive `DatabaseMcpServer` from the shell safely and repeatably.

## Mental Model

DatabaseMcpServer CLI has two state layers. Confusing them is the most common source of wasted time.

- **Config file** (`databases.json`): a static, human-edited list of named connections plus an `isDefault` flag. Changed by `config add / update / remove / use / clone / rename / import`.
- **CLI current-connection state** (`%USERPROFILE%/.database-mcp/cli-state.json`): a dynamic per-session pointer remembered across `tool` invocations. Changed by `tool switch_database`. Keyed by the *resolved* config path so two different config files keep independent current connections.

Consequences worth internalizing:

- `tool switch_database` does **not** rewrite `databases.json`. Users who expect "switch" to persist into the config file will be surprised; point them at `config use` instead.
- A fresh `tool` call reads cli-state first. It only falls back to `isDefault` when no state file exists or the saved name was removed from the config.
- `tool reload_database_config` reloads the config file but deliberately preserves the current connection when possible — this is a feature, not a bug.

When the user is confused about "which database am I actually on", run `tool get_current_database` and compare against `config list` / `config show` rather than trusting assumptions.

## Start Here

- Confirm the user wants CLI behavior, not MCP stdio transport. If the intent is "connect Claude Desktop / Cursor / Cline to this server", CLI is the wrong answer.
- `DatabaseMcpServer` is distributed as a .NET Global Tool. The user should have it on PATH via:

```powershell
dotnet tool install --global DatabaseMcpServer
dotnet tool list --global | Select-String databasemcpserver
```

- Verify the installed version via `dotnet tool list --global`, not via the binary. The CLI itself does not accept `--version` — it exits `2` with `未知命令: '--version'`. The .NET tool manifest is authoritative.
- If the installed version is stale or the tool misbehaves, upgrade before debugging:

```powershell
dotnet tool update --global DatabaseMcpServer
```

- If the `DatabaseMcpServer` command isn't found, `%USERPROFILE%\.dotnet\tools` (or the platform equivalent) is missing from `PATH` — tell the user to add it rather than trying to invoke a binary by absolute path.

## Command Families

| Family | Purpose | Typical use |
| --- | --- | --- |
| `DatabaseMcpServer tool <name>` | Invoke one MCP tool. Real work happens here. | Query, schema, DDL, export, health |
| `DatabaseMcpServer config <subcommand>` | Create, inspect, validate, import, export, repair connections inside a config file. | First-time setup, maintenance |
| `DatabaseMcpServer init` | Seed a default config file skeleton. | Bootstrapping a machine |
| `tool list` / `tool help <name>` / `config help <subcommand>` / `--help` | Discover exact parameter names before guessing. | Whenever naming is unclear |

Naming convention is strict: tool names are `snake_case`, options are `kebab-case`. When the CLI prints "最接近的命令" suggestions, prefer one of them — it means you got the naming form wrong.

## Handle Config Deliberately

- Prefer explicit `--config <path>` when the user has a target file. It makes behavior deterministic and keeps cli-state scoped correctly.
- If the user only gives a connection string, create a one-entry `databases.json` under `%TEMP%` rather than editing their real config.
- Keep secrets out of repo-tracked files unless the user explicitly asks to persist them.
- Delete temporary configs after testing if they contain credentials.
- For repeatable local usage, move the validated config to a stable path before handing it off.

Minimal ad-hoc config shape:

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

Config resolution rules (memorize or re-derive from help):

- `tool` without `--config` searches: `./databases.json` → `./local-databases.json` → `DB_CONFIG_PATH` → `%USERPROFILE%/.database-mcp/databases.json`.
- `init` and `config` default to `%USERPROFILE%/.database-mcp/databases.json` unless `--config` is supplied.

## Operating Principles

These are ordered from low to high risk. Don't skip ahead without a reason.

### Read-only first

Establishing trust before touching anything saves retries later:

1. `validate_configuration`
2. `test_connection` (or `test_connection_by_name` for a specific connection)
3. `get_database_config`
4. `list_databases` and `get_current_database` when connection identity matters
5. `health_check` when broader connectivity matters

### Schema before SQL

Guessing table or column names produces wrong SQL or silent mismatches. Before querying or changing objects:

1. `is_any_table` or `get_table_info_list`
2. `get_table_schema` or `get_column_infos_by_table_name`
3. `get_primaries`, `get_index_list`, or `get_trigger_names` when relevant

### Isolate writes and DDL

Write tools can damage real data, so isolate them:

- Never exercise write or schema tools on business tables unless the user explicitly asks.
- For verification loops, create uniquely prefixed temporary objects: `cli_<yyyyMMdd_HHmmss>_<shortid>`.
- Clean up created objects before finishing, and report cleanup status.
- Without explicit approval for writes, stop at read-only validation or deliver a plan only.

## Respect the CLI Contract

Classifying a failure correctly is half the diagnosis. The contract:

- Successful tool calls write their payload to `stdout` as JSON.
- Help text and CLI usage errors go to `stderr` (this includes root help and `tool list` — don't misclassify).
- Exit code `0`: success. Parse stdout JSON.
- Exit code `1`: tool ran and returned a structured failure — usually `success: false`. Backend / database / permission / capability issues live here.
- Exit code `2`: CLI usage failure. Missing `--yes`, missing option, unknown option, malformed JSON argument, quoting bug, config not found.
- `get_database_config` can return valid JSON on exit `0` without a top-level `success` field. Treat `exit 0 + parseable JSON` as success.

When triaging, first ask: did the tool even run? Exit `2` + stderr usage hint = it didn't, fix the command. Exit `1` + `success: false` = it did, look at the backend.

## Use `--yes` Correctly

`--yes` is an explicit acknowledgement that you accept the side effect. The CLI requires it for anything that mutates rows or schema:

- DML and batch execution
- Stored procedure calls (input-only and with-output)
- Table, column, index, constraint, remark, and default-value changes
- Drop, truncate, rename, backup, and similar destructive operations
- `config remove`

If the CLI prints `需要显式确认。请追加 '--yes'。`, the exit code is `2` and the diagnosis is CLI-layer: add `--yes`, don't chase a backend problem.

## Quote PowerShell Arguments Carefully

PowerShell quoting eats more debugging time than the database ever will. Guardrails:

- Wrap SQL and JSON arguments in **single quotes**. That disables PowerShell variable expansion and preserves literal content.
- Keep each JSON object or array as one argument — never split them across multiple `--foo` flags.
- For `batch_execute_commands`, pass one JSON array string: `'["cmd1","cmd2"]'`.
- For `execute_command_with_go`, pass one SQL argument containing embedded newlines and `GO`.
- For SQL Server `add_default_value`, the tool expects a SQL literal, not bare text. Escape the inner quotes: `--default-value '''active'''` (outer single quotes + doubled inner quotes).
- For scripted automation, prefer `ProcessStartInfo.ArgumentList` over composing a shell string — it sidesteps quoting entirely.

## Don't Weaken Security Quietly

Some CLI "fixes" work by downgrading the connection's security posture. Downgrades need explicit, informed consent — urgency is not consent.

When SQL Server connections fail with `Encrypt=True` and the error says encryption is required but unsupported on the machine:

1. **Report the exact error first.** The wording is misleading — on Microsoft.Data.SqlClient 4.0+ this is almost always a TLS 1.2 or server-cert-chain problem on the client, not literal "no crypto support".
2. **Don't silently swap in `Encrypt=False;TrustServerCertificate=True`.** That hides a real infrastructure problem and downgrades security.
3. **Offer ranked options, not a single fix.** Lead with the safest:
   1. Fix the client: enable TLS 1.2 in SCHANNEL, install the server's CA cert, update OS/driver.
   2. Keep encryption, skip cert check: `Encrypt=True;TrustServerCertificate=True`. Traffic still encrypted; only the server identity check is skipped. Acceptable on trusted intranet.
   3. Disable encryption entirely: `Encrypt=False`. Diagnostic fallback only — requires explicit consent and shouldn't land in the user's real config.
4. **Keep the tradeoff visible.** If the user says "just make it work", surface what they're giving up before changing the string. A temp config under `%TEMP%` is a safer diagnostic vehicle than editing their real `databases.json`.

The same pattern generalizes: if the next CLI quirk has a "silently downgrade" shortcut, rank by safety, name the tradeoff, get consent.

## Reference Index

Three bundled reference files, always available inside the skill folder. Route to the right one based on what the user is asking:

| User's question or symptom | Read first |
| --- | --- |
| "What's the exact command / option to X?" (syntax lookup) | `references/cli.md` §4 (config) / §8 (tool catalog) |
| "Walk me through installing and getting started." | `references/cli.md` §2 用户使用流程 |
| "My PowerShell command has weird quoting" or "why does `add_default_value` fail" | `references/commands.md` |
| "CLI returned this error / exit 1 / exit 2 / `需要显式确认`" | `references/troubleshooting.md` |
| "`switch_database` didn't change my default" or "which DB am I actually on" | Stay in this SKILL.md Mental Model + `references/troubleshooting.md` #10 |
| "Is this a CLI bug or a backend issue?" | `references/troubleshooting.md` Quick Triage table |

File summaries:

- `references/commands.md` — PowerShell quoting gotchas, the single-quote convention, `add_default_value` SQL-literal escape, `execute_command_with_go` newline trick, broad verification workflow, config-vs-current-connection summary.
- `references/troubleshooting.md` — symptom → diagnosis matrix for `--yes`, unknown command, config not found, stdout/stderr mix, JSON argument parsing, SQL Server quoting, encryption, `success:false` vs CLI failure, current-vs-default confusion, and `reload_database_config` semantics.
- `references/cli.md` — the authoritative CLI spec mirrored from the source repo's `Doc/cli.md`. §2 is the end-to-end user lifecycle; §3.6 covers the current-vs-default state model; §4 is config management; §6 is PowerShell traps; §7 lists every `--yes`-required command; §8 is the full tool catalog. When the skill lives inside the source repo, the upstream `Doc/cli.md` is canonical — keep this copy in sync if the repo file changes.

## Report Results Clearly

A verification or troubleshooting report is useful only if someone else can reproduce it. Use this exact structure when writing up a CLI run:

```
**Executable**: `DatabaseMcpServer` on PATH, version <from `dotnet tool list --global`>
**Config**: <absolute path>, source: <--config flag | DB_CONFIG_PATH | ./databases.json | ./local-databases.json | %USERPROFILE% default>
**Current connection**: <name from get_current_database>  (default in databases.json: <name>)
**Command**: <exact argv, one space per argument>
**Exit code**: <0 | 1 | 2>
**Stdout** (trimmed):
    <JSON payload, or "(empty)">
**Stderr** (trimmed):
    <help / warning / "(empty)">
**Classification**: <CLI-layer usage error | backend/tool failure (success: false) | success>
**Cleanup**: <temp config deleted at <path> | temp DB objects dropped: <list> | none required>
```

Fill every field, even with `(empty)` or `none required`. A blank field reads as "I forgot to check" and forces the reader to guess.
