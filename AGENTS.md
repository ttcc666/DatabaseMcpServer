# Repository Guidelines

## Project Structure & Module Organization
- `DatabaseMcpServer.csproj` and `Program.cs` sit at the root; keep runtime services in `Services/`, their interfaces in `Interfaces/`, and shared utilities (SqlSugar wiring, serialization, guards) in `Helpers/`.
- Implement MCP tools inside `Tools/Command`, `Tools/Management`, `Tools/Query`, or `Tools/Schema`, registering the class in `Program.cs`. This preserves SRP and lets new capabilities ship without editing existing tool code.
- `Filters/` contains cross-cutting concerns such as exception handling, while `.mcp/`, `mcp.json.example`, and `mcp.json.local` store client templates—never insert production secrets there or outside `.mcp/`.

## Build, Test, and Development Commands
- `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run` starts the stdio MCP server for smoke tests.
- `dotnet build` ensures the net9.0 executable compiles against every PackageReference; run it before pushing.
- `dotnet test` executes all test projects; add new xUnit/NUnit projects beside the solution root so the command succeeds without flags.
- `dotnet pack -c Release` emits the MCP NuGet/global tool package after a clean build.
- `DatabaseMcpServer --version` validates the globally installed CLI matches the local source.

## Coding Style & Naming Conventions
- Target C# 12 / .NET 9 with implicit usings and nullable reference types. Use four-space indentation, braces on the next line, and file-scoped namespaces when practical.
- Follow DI-first design (KISS + SOLID): inject services, avoid static state, and keep each tool/service focused on one concern.
- Use PascalCase for public members, `_camelCase` for private readonly fields, camelCase parameters, and save all files as UTF-8 without BOM.

## Testing Guidelines
- Prefer xUnit with `ClassUnderTestTests` naming; colocate new test projects at the repo root for automatic discovery.
- Favor in-memory or disposable providers when exercising database logic. If a real instance is required, guard it with opt-in env vars and document the prerequisite in the test README.
- A green `dotnet test` run is mandatory before commits/PRs; summarize which providers or fixtures were used when touching database access.

## Commit & Pull Request Guidelines
- Branch from main using `feature/<topic>`; write imperative, scoped commits (e.g., `Add schema diff tool`).
- PR descriptions must outline behavior changes, list impacted tools/services, cite test evidence (`dotnet test`, manual queries), and call out new environment variables like `SEQ_SERVER_URL` or `SEQ_API_KEY`.
- Link relevant issues, attach sample JSON responses or screenshots for observable changes, and double-check that secrets remain only in local environment variables.

## Security & Configuration Tips
- Configure credentials through env vars (`DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, `SEQ_API_KEY`) and keep `.mcp/` templates sanitized before committing.
- Reuse shared helpers for SQL sanitization and configuration validation, documenting any new variable or permission requirement in README before release.
