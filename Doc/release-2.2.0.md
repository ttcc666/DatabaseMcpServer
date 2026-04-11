# DatabaseMcpServer 2.2.0

## Release Title

`DatabaseMcpServer 2.2.0`

## GitHub Release Body

### Highlights

- Added CLI mode so the existing database tools can be called directly with `DatabaseMcpServer tool <tool_name>`.
- Preserved existing no-argument stdio MCP server behavior for MCP clients.
- Added a shared tool catalog so MCP registration and CLI discovery stay in sync.
- Added CLI docs, validation scripts, and a draft `database-mcp-cli` skill for repeatable command-line workflows.

### What's Changed

- New CLI runtime:
  - `tool list`
  - `tool help <tool_name>`
  - `--config`
  - `--yes`
- CLI command metadata and reflection-based dispatcher
- Silent CLI runtime output so successful tool calls emit JSON results only
- SQL Server-specific `add_default_value` handling for string defaults
- New docs:
  - `Doc/cli.md`
  - updated `README.md` / `README_EN.md`
- New verification assets:
  - `scripts/verify-cli-tools.ps1`
  - SQL Server / SQLite CLI validation coverage
- New skill draft:
  - `skills/database-mcp-cli/`

### Validation

```powershell
dotnet test 'D:\Demo\my-mcp\DatabaseMcpServer\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'
pwsh -NoLogo -NoProfile -File 'D:\Demo\my-mcp\DatabaseMcpServer\scripts\verify-cli-tools.ps1'
```

### Package

- NuGet package: `DatabaseMcpServer.2.2.0.nupkg`
- Local artifact path: `artifacts/release/DatabaseMcpServer.2.2.0.nupkg`

### Upgrade

```powershell
dotnet tool update --global DatabaseMcpServer --version '2.2.0'
```

Or with `dnx`:

```powershell
dnx DatabaseMcpServer@2.2.0 --yes
```

### Notes

- `2.2.0` is a feature release because it adds a new CLI execution path on top of the existing MCP server.
- Existing MCP client integrations remain compatible because `DatabaseMcpServer` without arguments still starts the stdio server.
