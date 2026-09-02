# Repository Guidelines

## Project Structure & Module Organization
- Keep solution-level files at the repository root, including `DatabaseMcpServer.slnx`, `global.json`, `nuget.config`, and MCP config examples.
- Main server code lives in `src/DatabaseMcpServer/`. Entry points and composition are in `Program.cs` and `Extensions/`.
- Group runtime logic by responsibility: `Services/` for implementations, `Interfaces/` for contracts, `Helpers/` for shared parsing/sanitization/configuration utilities, `Filters/` for cross-cutting MCP filters, and `Models/` for DTOs.
- Tool classes belong under `Tools/Command`, `Tools/Query`, `Tools/Documentation`, `Tools/Export`, or `Tools/Management`; keep each tool focused and single-purpose.
- CLI and local web configuration code live in `Cli/` and `Web/`. Tests are in `tests/DatabaseMcpServer.Tests/`. Documentation and database presets are in `Doc/` and `DatabaseSetting/`. The optional Vue UI is in `website/`.
- Desktop config editor: `DatabaseMcpServer.Gui.Avalonia/` (Avalonia) with shared services in `DatabaseMcpServer.Gui.Core/`. It uses `InternalsVisibleTo` so it edits the same `databases.json` the MCP server / CLI / `-web` read.

## Build, Test, and Development Commands
- `dotnet build 'DatabaseMcpServer.slnx'` — compile the full solution and validate package references.
- `dotnet test 'DatabaseMcpServer.slnx'` — run all xUnit tests discovered under `tests/`.
- `DB_CONNECTION_STRING=... DB_TYPE=MySql dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj'` — launch the stdio MCP server for a local smoke test.
- `dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c Release` — create the NuGet/global-tool package.
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` — run repository verification scripts when preparing releases.

## Coding Style & Naming Conventions
- Use C# 12 with nullable enabled, implicit usings, four-space indentation, file-scoped namespaces, and braces on the next line.
- Prefer constructor injection and small, testable classes. Reuse shared helpers for SQL safety, connection masking, JSON defaults, and argument validation.
- Use PascalCase for public types and members, `_camelCase` for private readonly fields, and camelCase for parameters/local variables.
- Keep comments minimal and rationale-focused. Save text files as UTF-8 without BOM.

## Testing Guidelines
- Framework: xUnit. Name test classes `ClassUnderTestTests` and test methods with clear behavior-focused names.
- Keep tests deterministic and isolated; mock or fake database-facing dependencies unless a test is explicitly integration-oriented.
- Document required environment variables before adding integration tests that need live databases.

## Commit & Pull Request Guidelines
- Branch from `main` using `feature/<topic>` or `fix/<topic>`.
- Write imperative commits, such as `Add schema export validation`, and mention behavioral impact when useful.
- Pull requests should summarize changes, list touched tools/services, link issues, and include verification evidence such as test output, sample JSON responses, or screenshots for UI changes.

## Security & Configuration Tips
- Never commit secrets. Provide credentials through `DB_CONNECTION_STRING`, `DB_TYPE`, `SEQ_SERVER_URL`, and `SEQ_API_KEY`, or local ignored config files.
- Mask connection strings in logs and responses. Route new SQL execution paths through existing sanitization and dangerous-operation guards.