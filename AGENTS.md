# Repository Guidelines

## Project Structure & Module Organization
- Root files: `DatabaseMcpServer.csproj` and `Program.cs` register MCP tools. Runtime services live in `Services/`; interfaces in `Interfaces/`; helpers (SqlSugar config, serialization guards, argument validators) in `Helpers/`.
- Tool implementations are grouped under `Tools/Command`, `Tools/Query`, `Tools/Schema`, `Tools/Management`, plus `ConnectionTools/` for environment switches. Keep each class single-purpose.
- Cross-cutting filters reside in `Filters/`. Client templates are in `.mcp/`, `mcp.json.example`, and `mcp.json.local`. Tests (xUnit) sit beside the solution root for automatic discovery.

## Build, Test, and Development Commands
- `dotnet build` — compile the net9.0 target and validate package references.
- `dotnet test` — run all xUnit projects; document required env vars before enabling integration suites.
- `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run` — launch the stdio MCP server for smoke testing.
- `dotnet pack -c Release` — produce the NuGet/global tool artifact.
- `DatabaseMcpServer --version` — verify an installed CLI matches current source.

## Coding Style & Naming Conventions
- C# 12, implicit usings, nullable enabled, four-space indentation, braces on the next line. Prefer file-scoped namespaces and constructor injection.
- Naming: PascalCase for public members; `_camelCase` for private readonly fields; camelCase for parameters.
- Keep comments minimal and rationale-focused; favor self-explanatory code. Encoding: UTF-8 (no BOM).

## Testing Guidelines
- Framework: xUnit. Name test classes `ClassUnderTestTests` and keep fixtures isolated/deterministic.
- Place tests beside the solution root; `dotnet test` should discover them without extra flags.
- Record any manual prerequisites (e.g., database env vars) in test READMEs before running integration tests.

## Commit & Pull Request Guidelines
- Branch from `main` using `feature/<topic>`. Commit messages in imperative mood (e.g., `Add schema diff tool`) and describe behavioral impact, touched tools/services, linked issues, and verification evidence.
- Pull requests should summarize impact, link issues, and include verification notes (tests, screenshots, sample JSON responses) when output changes.

## Security & Configuration Tips
- Never commit secrets. Supply credentials via env vars: `DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, `SEQ_API_KEY`.
- Reuse shared helpers for SQL sanitization and configuration validation; document any new variable or permission requirement before release.
