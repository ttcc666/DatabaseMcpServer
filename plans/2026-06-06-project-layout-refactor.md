# Project Layout Refactor Plan

## Goal

将当前 root-heavy 的 .NET MCP server 仓库整理为更标准的 solution layout：

- production C# project 放入 `src/DatabaseMcpServer/`
- xUnit test project 放入 `tests/DatabaseMcpServer.Tests/`
- root 保留 solution、docs、scripts、config examples、frontend workspace、repo metadata
- 不改变 runtime behavior（运行行为）、NuGet package identity、CLI config discovery、MCP tool surface

本计划只处理目录结构和路径引用，不做业务代码重构。

## Current State

当前 tracked production code 直接位于仓库根目录：

- `Program.cs`
- `Cli/`
- `Extensions/`
- `Filters/`
- `Helpers/`
- `Hosting/`
- `Interfaces/`
- `Models/`
- `Properties/`
- `Services/`
- `Strategies/`
- `Tools/`
- `Web/`
- `DatabaseMcpServer.csproj`

测试项目位于：

- `DatabaseMcpServer.Tests/`

仓库中已经存在 `src/DatabaseMcpServer/` 和 `tests/DatabaseMcpServer.Tests/`，但目前主要是 build output（`bin/obj`）和 IDE output，不是正式源码布局。

## Target Layout

```text
DatabaseMcpServer/
  DatabaseMcpServer.slnx
  global.json
  nuget.config
  README.md
  README_EN.md
  TOOLS.md
  LICENSE
  AGENTS.md
  CLAUDE.md
  databases.json.example
  local-databases.json
  mcp.json.example
  mcp.json.local
  .mcp/
  data/
  Doc/
  DatabaseSetting/
  plans/
  scripts/
  skills/
  website/
  src/
    DatabaseMcpServer/
      DatabaseMcpServer.csproj
      Program.cs
      Cli/
      Extensions/
      Filters/
      Helpers/
      Hosting/
      Interfaces/
      Models/
      Properties/
      Services/
      Strategies/
      Tools/
      Web/
  tests/
    DatabaseMcpServer.Tests/
      DatabaseMcpServer.Tests.csproj
      *.cs
```

## Scope

In scope（本次执行范围）：

- 移动 main project 文件和 production C# source 到 `src/DatabaseMcpServer/`
- 移动 test project 到 `tests/DatabaseMcpServer.Tests/`
- 更新 `DatabaseMcpServer.slnx`
- 更新 test `ProjectReference`
- 更新 `DatabaseMcpServer.csproj` 中 root-relative assets path：
  - `.mcp/server.json`
  - `README.md`
  - `LICENSE`
  - `website/dist/**`
  - `website` build inputs and npm working directory
- 更新 scripts 中 project/test 路径
- 更新 repo guidance docs 中仍指向旧 project/test 路径的命令
- 清理或忽略迁移前遗留的 untracked `src/*/bin`, `src/*/obj`, `tests/*/bin`, `tests/*/obj`

Out of scope（本次不做）：

- 不移动 `website/`。它是独立 Vite/Vue workspace，同时被 server embed，暂时保留 root 可以降低路径变更风险。
- 不移动 `Doc/`、`DatabaseSetting/`、`skills/`。这些路径已被 README/skill references 使用，后续可单独做 docs cleanup。
- 不改 namespace。保持 `DatabaseMcpServer.*`，避免 public/internal API churn。
- 不重命名 package、tool command、assembly name。
- 不改 CLI config discovery：`./databases.json`、`./local-databases.json` 的查找语义保持不变。

## Invariants

必须保持：

- `PackageId` 仍为 `DatabaseMcpServer`
- `ToolCommandName` 仍为 `DatabaseMcpServer`
- `PackageVersion` 不因本次目录重构改变
- `InternalsVisibleTo("DatabaseMcpServer.Tests")` 继续有效
- embedded web UI still resolves from logical path `website/dist`
- NuGet package includes root `README.md`, root `LICENSE`, and `.mcp/server.json`
- `dotnet build` from repo root works through solution/project path
- `dotnet test` from repo root discovers and runs tests
- `scripts/verify.ps1` remains copy-paste runnable from repo root or scripts folder
- no secrets or local config content are committed

## Implementation Checklist

1. Preflight（迁移前检查）
   - Run `git status --short`
   - Confirm no tracked user edits in files that will be moved
   - Record current tracked files with `git ls-files`

2. Remove generated output from target layout placeholders
   - Delete only generated directories under:
     - `src/DatabaseMcpServer/bin`
     - `src/DatabaseMcpServer/obj`
     - `tests/DatabaseMcpServer.Tests/bin`
     - `tests/DatabaseMcpServer.Tests/obj`
   - Keep any tracked or user-authored files if found

3. Move production project into `src/DatabaseMcpServer/`
   - Move `DatabaseMcpServer.csproj`
   - Move `Program.cs`
   - Move production source directories:
     - `Cli`
     - `Extensions`
     - `Filters`
     - `Helpers`
     - `Hosting`
     - `Interfaces`
     - `Models`
     - `Properties`
     - `Services`
     - `Strategies`
     - `Tools`
     - `Web`

4. Move tests into `tests/DatabaseMcpServer.Tests/`
   - Move all tracked files from `DatabaseMcpServer.Tests/`
   - Remove the old empty `DatabaseMcpServer.Tests/` directory if empty

5. Update project and solution paths
   - `DatabaseMcpServer.slnx`
     - `src/DatabaseMcpServer/DatabaseMcpServer.csproj`
     - `tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj`
   - `tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj`
     - ProjectReference to `..\..\src\DatabaseMcpServer\DatabaseMcpServer.csproj`

6. Update `src/DatabaseMcpServer/DatabaseMcpServer.csproj`
   - Use repo-root helper property:
     - `RepositoryRoot = $(MSBuildProjectDirectory)\..\..`
   - Update pack assets to root-relative paths:
     - `$(RepositoryRoot)\.mcp\server.json`
     - `$(RepositoryRoot)\README.md`
     - `$(RepositoryRoot)\LICENSE`
   - Preserve package paths:
     - `/.mcp/`
     - `/`
   - Update website paths:
     - embedded resources from `$(RepositoryRoot)\website\dist\**\*`
     - source inputs from `$(RepositoryRoot)\website\...`
     - npm working directory `$(RepositoryRoot)\website`
   - Remove old `DatabaseMcpServer.Tests/**` exclusions; tests are outside the project directory now.

7. Update scripts
   - `scripts/verify.ps1`
     - build `src\DatabaseMcpServer\DatabaseMcpServer.csproj`
     - test `tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj`
   - `scripts/verify-cli-tools.ps1`
     - build new project path
     - update `$buildOutput` to `src\DatabaseMcpServer\bin\...`

8. Update docs/guidance
   - `AGENTS.md`
   - `CLAUDE.md`
   - `README.md`
   - `README_EN.md`
   - `Doc/release-*.md` only where active commands would be misleading
   - Prefer minimal path updates; do not rewrite prose broadly.

9. Final cleanup
   - Check no tracked `.cs` files remain at root source folders
   - Check old empty directories are removed
   - Check `rg --files -g '*.cs'` shows production code under `src/` and tests under `tests/`

## Validation Strategy

Run from repo root:

```powershell
dotnet build 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' --framework 'net9.0'
dotnet test 'tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'
.\scripts\verify.ps1
dotnet pack 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -c 'Release' -o 'artifacts\layout-refactor-pack'
```

Optional checks:

```powershell
dotnet build 'DatabaseMcpServer.slnx' --framework 'net9.0'
rg -n 'DatabaseMcpServer\.csproj|DatabaseMcpServer\.Tests\\DatabaseMcpServer\.Tests\.csproj|website\\dist|website\\src|DatabaseMcpServer.Tests\\' -g '!bin/**' -g '!obj/**' -g '!.git/**'
```

Expected results：

- build succeeds for `net9.0`
- tests pass
- `verify.ps1` succeeds
- pack succeeds and includes `README.md`, `LICENSE`, `.mcp/server.json`, and embedded `website/dist` assets
- old root source directories no longer contain tracked C# files

## Rollback Notes

If validation fails due to path issues:

- Revert only the layout branch / commit, not unrelated user work.
- If failure is isolated to MSBuild asset paths, fix `RepositoryRoot` and item includes before reverting.
- If tests fail behaviorally after pure move, inspect for path-dependent tests or `AppContext.BaseDirectory` assumptions.
- Do not delete `website/dist` or config examples as part of rollback.

## Execution Notes

- Use `git mv` for tracked files where possible so history is easy to review.
- Use native PowerShell file operations only for generated `bin/obj` cleanup, after verifying resolved paths are under repo root.
- Keep the final diff focused on moves plus path references.
- Do not combine with namespace cleanup, docs restructuring, or frontend relocation.
