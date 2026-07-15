# DatabaseMcpServer 3.0.0

## Release Title

`DatabaseMcpServer 3.0.0`

## GitHub Release Body

### Highlights

- Add `batch_sql_query` for sequential execution of 1-5 independent read-only queries with per-query results.
- Fix pooled `SqlSugarScope` lifetime so tool calls no longer dispose shared clients; active and retired scopes are released when the Host shuts down.
- Move the application and test projects into the conventional `src/` and `tests/` layout.
- Refresh the 55-tool catalog, CLI documentation, package content, and `database-mcp-cli` skill.

### Breaking Changes

- Remove `export_query_to_excel`, `export_table_to_excel`, and `export_multiple_queries_to_excel`.
- Remove `generate_database_documentation` and its supporting services and database-specific strategies.
- Remove the `ClosedXML` dependency.
- `batch_execute_commands` remains sequential and non-transactional: earlier successful commands are not rolled back when a later command fails. Callers must inspect every `results[]` item.

Use `sql_query` or `batch_sql_query` for data retrieval and perform file export or documentation generation in an external workflow when needed.

### Validation

```powershell
& '.\scripts\verify.ps1'
& '.\scripts\verify-cli-tools.ps1'
dotnet pack 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -c 'Release' -o 'artifacts\release'
```

Release gates require all xUnit tests to pass, 55/55 CLI tools to be covered by the isolated SQLite smoke test, and the locally packed `3.0.0` tool to expose `batch_sql_query`.

Validation evidence from 2026-07-15:

- .NET 9 / .NET 10 build: 0 warnings, 0 errors.
- xUnit: 74 passed, 0 failed, 0 skipped.
- CLI verifier: exit 0, catalog 55, covered 55, no missing/unknown/duplicate cases.
- Batch semantics: two `batch_sql_query` items succeeded; `batch_execute_commands` returned success/failure/success and the successful writes remained persisted.
- SQLite fixture results: 41 commands exited 0; 14 database-specific operations returned the expected structured unsupported/failure payloads; `get_database_config` returned valid JSON without a top-level `success` field.
- Release package: version `3.0.0`, 267 entries, all required documentation files present.
- Isolated package install: catalog 55, `batch_sql_query` help available, removed `export_query_to_excel` reported as an unknown tool with exit 2.

### Package

- NuGet package: `DatabaseMcpServer.3.0.0.nupkg`
- Package contents include `.mcp/server.json`, `README.md`, `README_EN.md`, `TOOLS.md`, and `Doc/cli.md`.

### Upgrade

```powershell
dotnet tool update --global 'DatabaseMcpServer' --version '3.0.0'
```

Or with `dnx`:

```powershell
dnx 'DatabaseMcpServer@3.0.0' --yes
```

Before upgrading, replace any calls to the removed Excel export or database documentation tools.
