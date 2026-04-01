# DatabaseMcpServer 2.1.1

## Release Title

`DatabaseMcpServer 2.1.1`

## GitHub Release Body

### Highlights

- Added `reload_database_config` to reload `DB_CONFIG_PATH` configuration at runtime without restarting the MCP process.
- Refreshed cached SqlSugar clients during config reload so subsequent requests immediately use updated connection settings.
- Expanded automated tests for config reload, fallback behavior, failure rollback, and client recreation.

### What's Changed

- New MCP tool: `reload_database_config`
- New config reload result model and service/factory interfaces for runtime refresh
- Thread-safe config state updates in `DatabaseConfigService`
- Client pool reset support in `SqlSugarClientFactory`
- Updated MCP manifest, version metadata, and README/README_EN release notes

### Validation

```powershell
dotnet test 'D:\Demo\my-mcp\DatabaseMcpServer\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'
dotnet pack 'D:\Demo\my-mcp\DatabaseMcpServer\DatabaseMcpServer.csproj' -c 'Release' -o 'D:\Demo\my-mcp\DatabaseMcpServer\artifacts\release'
```

### Package

- NuGet package: `DatabaseMcpServer.2.1.1.nupkg`
- Local artifact path: `artifacts/release/DatabaseMcpServer.2.1.1.nupkg`

### Upgrade

```powershell
dotnet tool update --global DatabaseMcpServer --version '2.1.1'
```

Or with `dnx`:

```powershell
dnx DatabaseMcpServer@2.1.1 --yes
```

### Notes

- No breaking changes are introduced in `2.1.1`.
- Existing multi-database setups can call `reload_database_config` after editing `databases.json` to apply updated connection settings immediately.
