# Repository Guidelines

## Project Structure & Module Organization
- Root: `DatabaseMcpServer.csproj` and `Program.cs` sit at the top level; MCP tools register in `Program.cs`.
- Runtime services: `Services/`; interfaces: `Interfaces/`; helpers (SqlSugar config, serialization guards, argument validators): `Helpers/`.
- Tool implementations: `Tools/Command`, `Tools/Query`, `Tools/Schema`, `Tools/Management`, and `ConnectionTools` for environment swaps; keep each class single-purpose.
- Cross-cutting filters: `Filters/`. Client templates: `.mcp/`, `mcp.json.example`, `mcp.json.local`.
- Tests: xUnit projects live beside the solution root so `dotnet test` discovers them without extra flags.

## Build, Test, and Development Commands
- `dotnet build` — compile net9.0 target and validate package references.
- `dotnet test` — run all xUnit projects; document required env vars before invoking integration suites.
- `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run` — launch the stdio MCP server for smoke testing.
- `dotnet pack -c Release` — produce the NuGet/global tool artifact.
- `DatabaseMcpServer --version` — confirm an installed CLI matches current source.

## Coding Style & Naming Conventions
- C# 12, implicit usings, nullable enabled, four-space indentation, braces on the next line.
- Prefer file-scoped namespaces, constructor injection, and SRP-aligned tool classes.
- Naming: PascalCase for public members; `_camelCase` for private readonly fields; camelCase for parameters.
- UTF-8 (no BOM). Keep comments minimal and rationale-focused; favor self-explanatory code.

## Testing Guidelines
- Framework: xUnit; name test classes `ClassUnderTestTests`.
- Keep fixtures isolated; prefer disposable/opt-in database providers.
- Aim for deterministic output; record any manual prerequisites in test READMEs.
- Every behavioral change should cite a `dotnet test` run.

## Commit & Pull Request Guidelines
- Branch from `main` using `feature/<topic>`.
- Commits: imperative mood (e.g., `Add schema diff tool`); describe behavioral impact, touched tools/services, linked issues, and verification evidence.
- PRs: include impact summary, linked issues, verification notes (tests, screenshots, sample JSON responses) when output changes.

## Security & Configuration Tips
- Never commit secrets. Supply credentials via env vars: `DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, `SEQ_API_KEY`.
- Reuse shared helpers for SQL sanitization and configuration validation.
- Document any new variable or permission requirement before release.
