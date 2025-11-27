# Repository Guidelines

## Project Structure & Module Organization
`DatabaseMcpServer.csproj` and `Program.cs` sit at the root; runtime services belong in `Services/`, interface contracts in `Interfaces/`, and reusable plumbing (SqlSugar configuration, serialization guards, argument validators) in `Helpers/`. Register each MCP tool in `Program.cs` and keep the implementation inside `Tools/Command`, `Tools/Management`, `Tools/Query`, or `Tools/Schema` to preserve single-purpose classes. Cross-cutting filters live in `Filters/`, while `.mcp/`, `mcp.json.example`, and `mcp.json.local` store sanitized client templates. Add new xUnit projects beside the solution root so `dotnet test` discovers them without flags.

## MCP Tool Responsibilities
`CommandTools` executes data-changing SQL, wrapping transaction batches, stored procedures (with IN/OUT parameters), and SQL Server scripts that contain `GO` statements. `QueryTools` focuses on read patterns: strongly typed queries, scalar fetches, paginated results, IN-clause expansion, and dataset streaming via `GetDataReader`. `SchemaTools` manages structural concerns by exposing inventory endpoints (tables, views, triggers, indexes) plus DDL helpers for adding or dropping indexes, partitions, procedures, functions, and views. `ConnectionTools` governs environment swaps: testing active connections, switching named databases, enumerating configured targets, and surfacing configuration health summaries.

## Build, Test, and Development Commands
`dotnet build` compiles the net9.0 target and validates package references. `dotnet test` runs every xUnit project; document any required env vars before running integration suites. `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run` launches the stdio MCP server for smoke testing, while `dotnet pack -c Release` produces the NuGet/global tool artifact. `DatabaseMcpServer --version` ensures an installed CLI matches the current source.

## Coding Style & Naming Conventions
Target C# 12 with implicit usings, nullable reference types, and four-space indentation with braces on the next line. Prefer file-scoped namespaces, constructor-injected dependencies, and SRP-aligned tool classes. Use PascalCase for public members, `_camelCase` for private readonly fields, camelCase for parameters, and UTF-8 (no BOM) encoding. Keep comments minimal and focused on rationale.

## Testing Guidelines
Favor xUnit with the `ClassUnderTestTests` naming convention. Keep fixtures isolated, prefer disposable or opt-in database providers, and document manual prerequisites in test READMEs. Aim for deterministic output and ensure every behavior change cites a corresponding `dotnet test` run.

## Commit & Pull Request Guidelines
Branch from `main` using `feature/<topic>`, craft imperative commits (e.g., `Add schema diff tool`), and describe behavioral impact, touched tools/services, linked issues, and verification evidence in each PR. Include screenshots or sample JSON responses whenever changes affect observable output.

## Security & Configuration Tips
Never commit secrets; provide credentials via env vars such as `DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, and `SEQ_API_KEY`. Reuse shared helpers for SQL sanitization and configuration validation, and document any new variable or permission requirement before release.
