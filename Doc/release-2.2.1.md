# DatabaseMcpServer 2.2.1

## Release Title

`DatabaseMcpServer 2.2.1`

## GitHub Release Body

### Highlights

- Refined the `database-mcp-cli` skill so CLI-oriented requests trigger more reliably.
- Tightened the CLI command cheat sheet and troubleshooting matrix for command-line workflows.
- Added `agents/openai.yaml` metadata for the skill and updated release version metadata to `2.2.1`.

### What's Changed

- Skill updates:
  - improved trigger wording in `skills/database-mcp-cli/SKILL.md`
  - refined `references/commands.md`
  - refined `references/troubleshooting.md`
  - added `skills/database-mcp-cli/agents/openai.yaml`
- Release metadata updates:
  - `DatabaseMcpServer.csproj`
  - `.mcp/server.json`
  - `README.md`
  - `README_EN.md`

### Validation

```powershell
& 'D:\Demo\DatabaseMcpServer\scripts\verify.ps1'
dotnet pack 'D:\Demo\DatabaseMcpServer\DatabaseMcpServer.csproj' -c 'Release' -o 'D:\Demo\DatabaseMcpServer\artifacts\release'
```

### Package

- NuGet package: `DatabaseMcpServer.2.2.1.nupkg`
- Local artifact path: `artifacts/release/DatabaseMcpServer.2.2.1.nupkg`

### Upgrade

```powershell
dotnet tool update --global DatabaseMcpServer --version '2.2.1'
```

Or with `dnx`:

```powershell
dnx DatabaseMcpServer@2.2.1 --yes
```

### Notes

- `2.2.1` is a patch release focused on CLI skill/documentation quality and release metadata alignment.
- No runtime behavior changes were introduced for the MCP stdio server or CLI execution path.
