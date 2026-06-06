# Repository Guidelines

## Project Structure & Module Organization
- Solution-level files stay at the repository root. The MCP server project lives in `src/DatabaseMcpServer/` with `DatabaseMcpServer.csproj` and `Program.cs`.
- Runtime services live in `src/DatabaseMcpServer/Services/`; interfaces in `src/DatabaseMcpServer/Interfaces/`; helpers (SqlSugar config, serialization guards, argument validators) in `src/DatabaseMcpServer/Helpers/`.
- Tool implementations are grouped under `src/DatabaseMcpServer/Tools/Command`, `src/DatabaseMcpServer/Tools/Query`, `src/DatabaseMcpServer/Tools/Documentation`, `src/DatabaseMcpServer/Tools/Export`, and `src/DatabaseMcpServer/Tools/Management`. Keep each class single-purpose.
- Cross-cutting filters reside in `src/DatabaseMcpServer/Filters/`. Client templates are in `.mcp/`, `mcp.json.example`, and `mcp.json.local`. Tests (xUnit) live in `tests/DatabaseMcpServer.Tests/`.

## Build, Test, and Development Commands
- `dotnet build 'DatabaseMcpServer.slnx'` — compile the solution and validate package references.
- `dotnet test 'tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'` — run all xUnit tests; document required env vars before enabling integration suites.
- `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run --project 'src\DatabaseMcpServer\DatabaseMcpServer.csproj'` — launch the stdio MCP server for smoke testing.
- `dotnet pack 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -c Release` — produce the NuGet/global tool artifact.
- `dotnet tool list --global | Select-String databasemcpserver` — verify an installed CLI matches current source. The binary itself has no `--version` flag (exits 2 on it); the .NET tool manifest is authoritative.

## Coding Style & Naming Conventions
- C# 12, implicit usings, nullable enabled, four-space indentation, braces on the next line. Prefer file-scoped namespaces and constructor injection.
- Naming: PascalCase for public members; `_camelCase` for private readonly fields; camelCase for parameters.
- Keep comments minimal and rationale-focused; favor self-explanatory code. Encoding: UTF-8 (no BOM).

## Testing Guidelines
- Framework: xUnit. Name test classes `ClassUnderTestTests` and keep fixtures isolated/deterministic.
- Place tests under `tests/`; `dotnet test 'DatabaseMcpServer.slnx'` should discover them without extra flags.
- Record any manual prerequisites (e.g., database env vars) in test READMEs before running integration tests.

## Commit & Pull Request Guidelines
- Branch from `main` using `feature/<topic>`. Commit messages in imperative mood (e.g., `Add schema diff tool`) and describe behavioral impact, touched tools/services, linked issues, and verification evidence.
- Pull requests should summarize impact, link issues, and include verification notes (tests, screenshots, sample JSON responses) when output changes.

## Security & Configuration Tips
- Never commit secrets. Supply credentials via env vars: `DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, `SEQ_API_KEY`.
- Reuse shared helpers for SQL sanitization and configuration validation; document any new variable or permission requirement before release.
