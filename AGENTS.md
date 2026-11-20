# Repository Guidelines

## Project Structure & Module Organization
- Keep solution-level files at the repository root, including `DatabaseMcpServer.slnx`, `global.json`, `nuget.config`, and MCP config examples.
- Host code lives in `src/DatabaseMcpServer/`: entry points, composition, MCP tools, CLI, Web, filters and serialization.
- `src/DatabaseMcpServer.Core/` owns driver-free contracts and policies, grouped into `Configuration`, `Connections`, `Execution`, `Sql`, `Mongo`, `Schema`, `Results` and `Diagnostics`. It has no NuGet dependencies.
- `src/DatabaseMcpServer.Infrastructure/` groups implementations into `Configuration`, `Connections`, `Execution`, `Sql`, `Mongo`, `Schema`, `Providers` and `Results`. It references Core only; never reference Host or MCP SDK here. See `Doc/backend-architecture.md`.
- Put related models, interfaces and implementations in their owning feature module. Do not recreate project-wide `Models`, `Interfaces`, `Helpers`, `Services` or `Operations` folders. Split a growing module by responsibility, such as `Configuration/Persistence`, `Configuration/Runtime`, or `Sql/Clients`, `Sql/Queries`, `Sql/Commands`, `Sql/Parameters`, `Sql/Safety`.
- Tool classes belong under `Tools/Connections`, `Tools/Schema`, `Tools/Sql` or `Tools/Mongo`; keep each tool focused and single-purpose. Shared tool metadata and invocation remain directly under `Tools`.
- CLI and local web configuration code live in `Cli/` and `Web/`. Tests in `tests/DatabaseMcpServer.Tests/` follow the corresponding feature modules; reusable fixtures live in `Support`, real-database tests in `Integration/Sql` and `Integration/Mongo`. Documentation and database presets are in `Doc/` and `DatabaseSetting/`. The optional Vue UI is in `website/`.

## Build, Test, and Development Commands
- `dotnet build 'DatabaseMcpServer.slnx'` — compile the full solution and validate package references.
- `dotnet test 'DatabaseMcpServer.slnx'` — run all xUnit tests discovered under `tests/`.
- `$env:DB_CONFIG_PATH = 'path/to/databases.json'; dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' --framework 'net10.0'` — launch the stdio MCP server with a local config file.
- `dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c Release '-p:TargetFrameworks=net10.0' '-p:BuildWebsite=true' --artifacts-path 'artifacts/package-build'` — create the .NET 10 NuGet/global-tool package with isolated restore assets; source builds/tests retain both frameworks. Regular backend builds use tracked `website/dist` without running npm.
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
- Never commit secrets. Use an ignored config file selected by `DB_CONFIG_PATH`; logging credentials use `SEQ_SERVER_URL` and `SEQ_API_KEY`.
- Mask connection strings in logs and responses. Route new SQL execution paths through existing sanitization and dangerous-operation guards.
