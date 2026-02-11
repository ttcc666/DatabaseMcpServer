# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DatabaseMcpServer is a .NET MCP (Model Context Protocol) server published as a .NET Global Tool on NuGet. It gives AI assistants structured access to 19+ database types via 57 MCP tools over stdio transport. The core ORM is **SqlSugar**.

## Build & Run Commands

```bash
# Build (multi-target: net9.0 + net10.0)
dotnet build

# Run locally (requires DB_CONFIG_PATH env var pointing to databases.json)
dotnet run --project D:\Demo\my-mcp\DatabaseMcpServer

# Package as NuGet global tool
dotnet pack -c Release

# Verify installed CLI version
DatabaseMcpServer --version
```

There are no test projects in the repository. AGENTS.md references xUnit but no tests exist yet.

## Environment Variables

| Variable | Required | Purpose |
|----------|----------|---------|
| `DB_CONFIG_PATH` | Yes | Absolute path to `databases.json` config file |
| `SEQ_SERVER_URL` | No | Seq log server URL |
| `SEQ_API_KEY` | No | Seq API key |
| `DB_DDL_WHITELIST` | No | Semicolon-separated regex patterns to whitelist DDL operations |

Legacy v1.x env vars (`DB_CONNECTION_STRING`, `DB_TYPE`) are rejected at startup with an error.

## Architecture

### Single-project monolith

Everything lives in one `.csproj` — no library projects or test projects. Multi-targets `net9.0` and `net10.0`.

### Startup flow (Program.cs)

1. Creates Generic Host, configures Serilog (console to stderr, optional Seq sink)
2. Warmup: pre-loads SqlSugar provider assemblies (ClickHouse, MongoDB, GaussDB, OceanBase) to avoid single-file publish failures
3. Registers DI singletons: `DatabaseHelper`, `DatabaseConfigService`, `DatabaseDocumentationStrategyFactory`, `DatabaseDocumentationService`
4. Registers MCP server with stdio transport and 6 tool classes
5. Runs the host

### Key directories

- **Tools/** — MCP tool implementations, split by concern:
  - `Management/ConnectionTools.cs` (9 tools) — connection testing, switching, health checks
  - `Management/SchemaTools.cs` (34 tools) — table/column/index/constraint CRUD via `DbMaintenance`
  - `Query/QueryTools.cs` (5 tools) — SELECT queries, scalar, multi-result-set
  - `Command/CommandTools.cs` (5 tools) — INSERT/UPDATE/DELETE, stored procedures, batch execution
  - `Export/ExcelExportTools.cs` (3 tools) — Excel export via ClosedXML
  - `Documentation/DocumentationTools.cs` (1 tool) — database documentation generation
- **Services/** — `DatabaseConfigService` (loads `databases.json`, manages SqlSugarScope connection pool with double-check locking), `DatabaseDocumentationService`
- **Strategies/** — Strategy pattern in two areas:
  - `DBSetting/` — 22 `IDatabaseOptimizationStrategy` implementations (one per DB type) + factory. Applied when creating SqlSugar clients.
  - `Documentation/` — `IDatabaseDocumentationStrategy` implementations (MySQL, PostgreSQL, SQL Server, Oracle, default) + factory
- **Helpers/** — `DatabaseHelper` handles SQL parsing, JSON serialization of results, dangerous operation detection (regex-based DDL blocking), parameter binding
- **Models/** — `DatabaseConnection`, `DatabasesConfig`, `ApiResult<T>`, `DatabaseMcpException` with `DatabaseErrorCode` enum
- **Filters/** — `McpExceptionFilter` for centralized error handling
- **Interfaces/** — Service contracts (`IDatabaseConfigService`, `IDatabaseHelperService`, `IDatabaseDocumentationService`)

### How MCP tools work

Each tool method is decorated with `[McpServerTool]` and `[Description]`. The typical pattern:
1. Get a `SqlSugarScope` client from `_databaseConfig.CreateClient()`
2. Call SqlSugar's `DbMaintenance`, `Ado`, or `Queryable` API
3. Serialize results via `_databaseHelper.SerializeResult()`
4. Exceptions caught by `McpExceptionFilter.HandleException()`

### Adding a new database type

1. Create a new `XxxOptimizationStrategy : IDatabaseOptimizationStrategy` in `Strategies/DBSetting/`
2. Register it in `DatabaseOptimizationStrategyFactory`
3. Add a `DbType` string mapping in `DatabaseHelper.ParseDbType()`
4. (Optional) Add a documentation strategy in `Strategies/Documentation/`
5. Add a configuration guide in `DatabaseSetting/`

See `Doc/extending-database-optimization.md` for details.

### Security model

- SQL sent to `SqlQuery` / `ExecuteCommand` is checked against dangerous operation regex patterns (DROP TABLE, DROP DATABASE, TRUNCATE TABLE, ALTER TABLE, CREATE TABLE)
- Blocked unless matched by `DB_DDL_WHITELIST` regex
- Parameters are bound as `SugarParameter[]` to prevent injection
- Connection string passwords are masked in config summaries

## Coding Conventions

- C# 12, implicit usings, nullable enabled, file-scoped namespaces
- Four-space indentation, braces on next line
- PascalCase for public members, `_camelCase` for private readonly fields, camelCase for parameters
- Constructor injection, all services registered as singletons
- UTF-8 encoding (no BOM)

## Configuration

`databases.json` structure — array of database connections with optional per-DB optimization settings:

```json
{
  "databases": [
    {
      "name": "my-db",
      "connectionString": "...",
      "dbType": "MySql",
      "description": "...",
      "isDefault": true,
      "optimizationSettings": { "enableBulkCopy": "true" }
    }
  ]
}
```

See `databases.json.example` for all 23 supported DB types and their optimization options. Per-database configuration docs are in `DatabaseSetting/`.

## Commit Conventions

- Branch from `main` using `feature/<topic>`
- Imperative mood commit messages (e.g., "Add schema diff tool")
- Include behavioral impact, touched tools/services, linked issues, and verification evidence
