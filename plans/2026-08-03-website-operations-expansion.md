# Website Operations Expansion Plan

- Date: 2026-08-03
- Status: Completed
- Scope: `website/` and the local-only ASP.NET Core Web host under `src/DatabaseMcpServer/Web/`
- Requested features: connection search/filter/sort, all-connection health check, connection-string wizard, dark mode, MCP Tool Playground

## Goal

Extend the existing configuration workbench into a safer local operations console without changing the `databases.json` schema or weakening the existing SQL/tool safeguards.

The implementation should:

- make a growing connection list easy to scan and operate;
- expose the existing all-connection health check with useful status and latency feedback;
- guide connection-string entry while retaining an expert raw mode;
- support light, dark, and system themes without a flash of the wrong theme;
- expose the registered MCP tools through a catalog-driven local playground;
- keep stored credentials masked and keep dangerous tool invocation explicitly gated.

## Assumptions And Decisions

1. The Web host remains bound to loopback only. Remote hosting, authentication, and multi-user access are out of scope.
2. The app remains a Vite/Vue single-page application. Do not add Vue Router for two workspaces; use a top-level `connections | playground` workspace switch so the root remains small and the dependency surface stays stable.
3. Filtering and sorting are client-side because `/api/dashboard` already returns the complete connection list. Pagination is not part of this change.
4. Health checks reuse `ConnectionTools.HealthCheck()` and do not write configuration or database data.
5. The connection-string wizard supports every preset through cataloged connection-string families. Key/value formats use `DbConnectionStringBuilder`; URI formats such as MongoDB use a dedicated URI builder. Unsupported/custom formats always retain raw mode.
6. Existing saved connection strings are never returned unmasked. In edit mode, switching from “unchanged” to wizard/raw replacement requires a complete replacement value, including re-entering sensitive fields.
7. MCP Tool Playground means listing and invoking the in-process tools already registered by `DatabaseMcpToolCatalog`. It is not an MCP client/transport debugger and will not connect to arbitrary remote MCP servers.
8. Tool results and arguments are held in memory only. Do not persist query results, SQL, credentials, or tool history to `localStorage`.

## Scope

### In Scope

- Search by connection name, type, and description.
- Database-type filter and status filter (`default`, `current`, `dangerous`, `healthy`, `unhealthy`, `unchecked`).
- Sorting by name, type, status, and health latency.
- “Check all” health action, aggregate summary, per-row health/latency/error details.
- Catalog-driven connection-string wizard with raw-mode fallback.
- Light/dark/system theme selection persisted locally.
- Tool catalog search/category/risk filtering, generated typed parameter form, invocation, result display, copy/clear actions, and dangerous-tool confirmation.
- Backend and frontend tests, updated CLI/Web documentation, rebuilt embedded Web assets.

### Out Of Scope

- Arbitrary remote MCP server discovery or transport inspection.
- User accounts, remote network binding, shared sessions, or authorization roles.
- Query history persisted to disk/browser storage.
- SQL autocomplete, schema explorer, query result editing, or a dedicated SQL IDE.
- Parallel health-check execution or retry policies beyond the existing tool behavior.
- Changes to `databases.json` or `cli-state.json` formats.

## Invariants（不变量）

### Security

- `CliWebHost` must continue to listen on `localhost`/loopback only.
- API metadata must never include raw stored connection strings or secret field defaults.
- Password-like keys (`password`, `pwd`, credentials embedded in URIs) must be masked in previews, logs, errors, and responses that represent stored configuration.
- Tool names and target method/type must come exclusively from `CliToolCatalog`; request data must never select an arbitrary CLR type or method.
- Unknown parameters, invalid types, and oversized invocation bodies must be rejected before tool resolution.
- `CliWriteProtection.RequiresConfirmation(toolName)` remains the server-side source of truth. Protected tools require an exact tool-name confirmation in the request even if the UI is bypassed.
- Playground requests use JSON, a same-origin custom header, and origin/host validation when an `Origin` header is present. Do not enable permissive CORS.
- Tool output is rendered as escaped text/structured JSON, never through `v-html`.
- Browser cancellation may stop waiting for a response, but the UI must not claim that a synchronous database operation was cancelled unless the backend actually supports cancellation.

### Compatibility

- Existing add/edit/preset/raw connection-string requests remain valid.
- Existing CLI `DatabaseMcpServer tool ...` names, option binding, `--yes` behavior, exit codes, and stdout/stderr contracts remain unchanged.
- Default/current connection semantics remain unchanged.
- Existing masked dashboard/detail responses remain masked.
- The UI remains usable when health data has not been collected and when a database type has no specialized wizard profile.

### Vue/Data Flow

- Vue 3 Composition API with `<script setup lang="ts">` remains the standard.
- Route/root-level components are composition surfaces. Feature state lives in focused composables.
- Props are read-only; child mutations use typed emits or `defineModel` for true two-way form contracts.
- Source state is minimal; filtered/sorted rows and derived summaries use `computed`/TanStack row models rather than watcher-assigned duplicate state.

## High-Level Architecture

```mermaid
flowchart LR
    App[website/src/App.vue<br/>workspace composition] --> Header[components/app/AppHeader.vue<br/>workspace + theme]
    App --> Config[components/ConfigWorkbench.vue]
    App --> Playground[components/playground/ToolPlayground.vue]

    Config --> Table[components/connections/ConnectionTableCard.vue]
    Table --> Filters[ConnectionToolbar.vue]
    Table --> Rows[ConnectionDataTable.vue]
    Config --> Editor[components/EditorSheet.vue]
    Editor --> Wizard[components/connection-editor/ConnectionStringWizard.vue]

    Filters --> DashboardApi[/api/dashboard]
    Rows --> HealthApi[POST /api/databases/health-check]
    Wizard --> ProfileApi[GET /api/connection-string-profiles/{dbType}]
    Playground --> ToolCatalogApi[GET /api/tools]
    Playground --> ToolInvokeApi[POST /api/tools/{name}/invoke]

    HealthApi --> HealthTool[Tools/Management/ConnectionTools.HealthCheck]
    ToolCatalogApi --> Catalog[Cli/CliToolCatalog]
    ToolInvokeApi --> Invoker[Cli/CliToolInvoker]
    Invoker --> Registered[DatabaseMcpToolCatalog registered tools]
```

## Component Map

| Artifact | Single responsibility | Contract |
| --- | --- | --- |
| `App.vue` | Compose the app shell and active workspace | owns `activeWorkspace`; passes no database state |
| `components/app/AppHeader.vue` | Show product identity, workspace switch, and theme control | `workspace` model; emits workspace changes |
| `components/app/ThemeMenu.vue` | Select light/dark/system theme accessibly | consumes `useTheme()`; no parent mutation |
| `composables/useTheme.ts` | Own and persist color-mode state | readonly `mode`, `isDark`; actions `setTheme`, `toggleTheme` |
| `components/connections/ConnectionTableCard.vue` | Compose connection toolbar, table, health summary, and row actions | dashboard/health props; typed action emits |
| `components/connections/ConnectionToolbar.vue` | Search, type/status filters, sort choice, reset, health-check command | typed models for query/type/status/sort; emits `health-check` |
| `components/connections/ConnectionDataTable.vue` | Render TanStack rows and sorting affordances | rows/selection/busy props; re-emits row commands |
| `components/connections/ConnectionHealthSummary.vue` | Present aggregate health counts and last checked time | health response prop only |
| `composables/useConnectionTable.ts` | Configure TanStack filtering/sorting and merge health state | accepts readonly connections and health map; exposes row model/filter state |
| `composables/useConnectionHealth.ts` | Invoke/check/clear health results | readonly response/map/loading; action `checkAll` |
| `components/connection-editor/ConnectionStringWizard.vue` | Render catalog-driven structured fields and raw-mode fallback | draft/profile props or model; emits replacement payload |
| `components/connection-editor/ConnectionFieldControl.vue` | Render one typed field (text/password/number/boolean) | field definition + model; no API access |
| `composables/useConnectionStringWizard.ts` | Load profiles, manage wizard/raw mode, validate required fields | readonly profile/loading/errors; explicit update actions |
| `components/playground/ToolPlayground.vue` | Compose catalog, form, confirmation, and result panels | owns only feature composition |
| `components/playground/ToolCatalogPanel.vue` | Search/filter/select tools | tools/selected props; emits `select` |
| `components/playground/ToolParameterForm.vue` | Generate typed inputs from parameter metadata | parameter definitions + argument model; emits `invoke` |
| `components/playground/ToolResultPanel.vue` | Display escaped JSON/text output and timing | result/loading props; emits `copy`/`clear` |
| `components/playground/DangerousToolDialog.vue` | Require exact-name confirmation for protected tools | tool name/open props; emits confirmed name or cancel |
| `composables/useToolPlayground.ts` | Load catalog, validate arguments, invoke tools, hold ephemeral result | readonly catalog/result/loading; select/invoke/abort/clear actions |

## API Contracts

### `POST /api/databases/health-check`

Reuses `ConnectionTools.HealthCheck()` and returns the existing payload as a typed Web contract:

```json
{
  "success": true,
  "overallHealth": true,
  "totalConnections": 2,
  "healthyConnections": 2,
  "unhealthyConnections": 0,
  "results": [
    {
      "name": "sqlite-local",
      "dbType": "Sqlite",
      "isHealthy": true,
      "responseTimeMs": 12,
      "errorMessage": "",
      "checkedAt": "2026-08-03T12:00:00Z"
    }
  ]
}
```

Rules:

- Keep the existing sequential execution semantics.
- A failed connection is a result item, not an HTTP transport failure.
- Configuration/tool exceptions return the existing `{ success: false, message }` shape.

### `GET /api/connection-string-profiles/{dbType}`

Returns a non-secret form schema:

```json
{
  "success": true,
  "profile": {
    "dbType": "MySql",
    "format": "keyValue",
    "supportsWizard": true,
    "fields": [
      {
        "key": "Server",
        "label": "服务器",
        "inputType": "text",
        "required": true,
        "sensitive": false,
        "defaultValue": "localhost"
      },
      {
        "key": "Password",
        "label": "密码",
        "inputType": "password",
        "required": true,
        "sensitive": true,
        "defaultValue": null
      }
    ]
  }
}
```

Rules:

- Profiles are cataloged by database family to avoid duplicating 24 nearly identical definitions.
- Sensitive defaults are always `null`, even when preset examples contain demo passwords.
- Unknown types return `supportsWizard: false` so raw mode still works.

### Existing Create/Update Requests

Extend Web-only DTOs additively with an optional structured connection payload:

```json
{
  "connectionString": null,
  "connectionFields": {
    "Server": "localhost",
    "Port": "3306",
    "Database": "app",
    "User": "root",
    "Password": "secret"
  }
}
```

Rules:

- Exactly one of raw `connectionString` or `connectionFields` is accepted when a replacement is requested.
- The backend builds/escapes the final value through a structured builder before handing it to `CliConfigCommandHandler`.
- On edit, omitting both keeps the current connection string unchanged.
- The response never echoes the resulting raw connection string.

### `GET /api/tools`

Returns DTOs projected from `CliToolCatalog`:

```json
{
  "success": true,
  "tools": [
    {
      "name": "get_table_schema",
      "description": "...",
      "category": "schema",
      "requiresConfirmation": false,
      "parameters": [
        {
          "name": "tableName",
          "optionName": "table-name",
          "description": "Table name",
          "type": "string",
          "required": true,
          "defaultValue": null
        }
      ]
    }
  ]
}
```

Do not serialize `Type`, `MethodInfo`, CLR type names, or internal registration details.

### `POST /api/tools/{toolName}/invoke`

```json
{
  "arguments": {
    "table-name": "users"
  },
  "confirmation": null
}
```

Response:

```json
{
  "success": true,
  "toolName": "get_table_schema",
  "durationMs": 18,
  "result": {}
}
```

Rules:

- Resolve `toolName` only through `CliToolCatalog`.
- Reject unknown arguments and perform the same string/int/bool/JSON conversion used by CLI mode.
- Protected tools require `confirmation` to equal the exact tool name.
- Preserve tool-level `{ success: false }` as an invocation result; reserve HTTP 400/404 for malformed requests/unknown tools.
- Require `Content-Type: application/json` and `X-DatabaseMcp-Web: 1`; validate same-origin when `Origin` is supplied.
- Limit the request body to a documented local-console ceiling (proposed: 1 MiB).
- Serialize invocations in the Web host so global current-database state cannot race between concurrent calls.
- Never log argument or result bodies.

## Implementation Checklist

### 0. Baseline And Shared Contracts

- [x] Run the existing frontend build and solution tests before edits; save failures as baseline evidence.
- [x] Add frontend API response types for connection health, connection profiles/fields, tool metadata, invocation request/result, and workspace/theme modes in focused type modules instead of growing `types.ts` indefinitely.
- [x] Extract `fetchJson`/HTTP error handling from `useConfigWorkbench.ts` into `website/src/api/http.ts`; preserve multipart import and file download behavior.
- [x] Add the first-party `X-DatabaseMcp-Web` header to JSON mutation requests without attaching it to static/file downloads.

### 1. Search, Type/Status Filters, And Sorting

- [x] Add `useConnectionTable.ts` with TanStack Vue Table state for global search, database-type filter, status filter, and sorting.
- [x] Search normalized name, type, and description; trim whitespace and use case-insensitive matching.
- [x] Derive unique type options from dashboard data.
- [x] Define stable status precedence for sorting: unhealthy, healthy, current, default, dangerous, unchecked/normal. Document it in a unit test.
- [x] Split the existing `ConnectionTableCard.vue` into toolbar/data-table/row-action responsibilities while preserving all existing commands.
- [x] Add sortable headers with correct `aria-sort`, visible result count, reset filters, “no matches” state distinct from “no connections,” and responsive controls.
- [x] Preserve the selected connection when it remains visible; do not silently mutate selection when filtering hides it.

### 2. All-Connection Health Check

- [x] Add Web DTOs for the existing health payload.
- [x] Add `CliWebApiService.HealthCheck()` (or a focused health service if the API service becomes too large) that resolves `ConnectionTools` and returns its payload without reimplementing database checks.
- [x] Map `POST /api/databases/health-check` in `CliWebHost.ConfigureApplication`.
- [x] Add `useConnectionHealth.ts`; normalize results into a readonly map keyed with ordinal connection names.
- [x] Add a “全部检查” toolbar command, aggregate summary, last-checked time, per-row health badge, latency, error tooltip, loading state, and retry action.
- [x] Clear stale entries when connections are removed/renamed; mark new connections as unchecked.
- [x] Do not auto-run health checks on page load because that may contact every configured environment unexpectedly.

### 3. Connection-String Wizard

- [x] Add a connection-profile catalog that groups compatible databases (MySQL family, PostgreSQL family, SQL Server, Oracle family, SQLite/DuckDB, MongoDB URI, ODBC/specialized key-value, raw fallback).
- [x] Add a structured builder service using `DbConnectionStringBuilder` for key/value formats and a dedicated URI builder for MongoDB-style URIs.
- [x] Validate required fields, integer ports, URI components, duplicate keys, and mutually exclusive authentication options server-side.
- [x] Ensure profile API output has no password defaults and builder errors do not repeat secret values.
- [x] Extend create/preset/update Web DTOs with optional `connectionFields`; preserve `connectionString` compatibility.
- [x] Add `ConnectionStringWizard.vue` with a segmented wizard/raw mode, typed controls, password visibility icon with tooltip, advanced-field section, and a masked preview.
- [x] Refactor `EditorSheet.vue` away from direct prop mutation by using a typed model/local draft and explicit submit payload.
- [x] In edit mode, show “保持现有连接串” by default. Require a complete replacement and explicit re-entry of sensitive values when the user enables changes.
- [x] Keep raw mode available for every database type and preserve preset workflows.
- [x] Test delimiter escaping, values containing semicolons/equal signs, Oracle descriptors, blank passwords, MongoDB percent-encoding, and unsupported-type fallback.

### 4. Dark Mode

- [x] Add `useTheme.ts` using existing `@vueuse/core` with `light`, `dark`, and `auto/system` modes and storage key `dbmcp-color-mode`.
- [x] Add an early theme initializer in `website/index.html` to avoid FOUC（主题闪烁） before Vue mounts.
- [x] Add `ThemeMenu.vue` using Lucide `Sun`, `Moon`, and `Monitor` icons, an accessible icon button, tooltip, and dropdown selection.
- [x] Mount theme controls in `AppHeader.vue`; do not put theme state into `useConfigWorkbench`.
- [x] Audit hard-coded colors, focus/hover states, diagnostics console, badges, sheets, dialogs, and browser native controls in both themes.
- [x] Respect system preference changes only while the selected mode is `auto`.

### 5. Shared Tool Invocation Layer

- [x] Extract CLI argument conversion and reflection invocation from `CliRunner` into a focused internal `CliToolInvoker`/binder reused by both CLI and Web paths.
- [x] Preserve CLI usage messages, option names, confirmation checks, return types, exit-code classification, and tests exactly.
- [x] Register `CliToolCatalog` and the Web invocation service with intentional lifetimes; use a Web-only semaphore to serialize browser invocations.
- [x] Project public tool metadata DTOs from the catalog and map tool categories from registered tool types (`connection`, `schema`, `query`, `command`).
- [x] Add request validation for unknown tools/arguments, missing required arguments, type conversion, JSON parsing, confirmation, body size, content type, and first-party/same-origin headers.
- [x] Map `GET /api/tools` and `POST /api/tools/{toolName}/invoke` without exposing general reflection endpoints.
- [x] Parse JSON tool output into structured `result`; wrap non-JSON output as text without altering it.

### 6. MCP Tool Playground UI

- [x] Turn `App.vue` into a thin app shell with `AppHeader`, connection workbench, and Tool Playground workspace composition.
- [x] Build `useToolPlayground.ts` with catalog loading, selected tool, typed argument state, client-side required-field validation, `AbortController` for browser request cancellation, and ephemeral result state.
- [x] Build catalog search and category/risk filters. Display descriptions and protected-tool badges for quick scanning.
- [x] Generate parameter controls by metadata type: input/textarea for strings, number input for integers, switch for booleans, JSON textarea with parse feedback for JSON.
- [x] Add protected-tool AlertDialog that requires typing the exact tool name. Send confirmation only after a match; keep the server check authoritative.
- [x] Display execution state, duration, tool-level success/failure, formatted escaped JSON/text, copy result, and clear result.
- [x] Disable duplicate invocation while a request is active and state clearly that closing/cancelling the browser wait may not cancel database work.
- [x] Never persist arguments/results; clear sensitive form state when changing tools or leaving the Playground.

### 7. UI Components And Design Integration

- [x] Add only missing shadcn-vue primitives needed by the design, expected to include `switch` and `tooltip`; keep `components.json` style and aliases unchanged.
- [x] Use Lucide icons for icon actions and accessible tooltips for unfamiliar icon-only controls.
- [x] Keep cards limited to real tools/items; do not nest cards or wrap full page sections in decorative cards.
- [x] Verify stable toolbar/table dimensions, wrapping, and no text overlap at desktop and mobile widths.
- [x] Keep the operational palette neutral and ensure health/risk states are distinguishable without relying on color alone.

### 8. Tests, Documentation, And Packaging

- [x] Add backend unit tests for connection profile/build behavior and secret handling.
- [x] Extend `CliToolCatalogTests`/CLI runner tests to prove the extracted invoker preserves current CLI contracts.
- [x] Extend `CliWebHostTests` with health endpoint, tool catalog, read-only SQLite tool invocation, unknown tool, invalid argument, and rejected unconfirmed protected tool cases.
- [x] Execute protected-tool positive-path tests only against uniquely named objects in a temporary SQLite database and clean them up.
- [x] Add Vitest + Vue Test Utils with focused tests for filtering/sorting, health merge/stale removal, wizard validation/payload construction, theme mode, and Playground argument serialization/confirmation.
- [x] Update `Doc/cli.md` with the Web workspace capabilities and explicitly document that Playground invokes the same local registered tool catalog and confirmation policy.
- [x] Rebuild `website/dist` so embedded assets match source; do not hand-edit generated files.
- [x] Run Playwright visual/manual QA in both themes and both workspaces at desktop/mobile viewports.

## Expected Files

### Existing Files To Modify

- `src/DatabaseMcpServer/Cli/CliRunner.cs`
- `src/DatabaseMcpServer/Cli/CliToolMetadata.cs`
- `src/DatabaseMcpServer/Extensions/ServiceCollectionExtensions.cs`
- `src/DatabaseMcpServer/Web/CliWebApiModels.cs`
- `src/DatabaseMcpServer/Web/CliWebApiService.cs`
- `src/DatabaseMcpServer/Web/CliWebHost.cs`
- `tests/DatabaseMcpServer.Tests/CliToolCatalogTests.cs`
- `tests/DatabaseMcpServer.Tests/CliRunnerTests.cs`
- `tests/DatabaseMcpServer.Tests/CliWebHostTests.cs`
- `website/index.html`
- `website/package.json`
- `website/package-lock.json`
- `website/src/App.vue`
- `website/src/types.ts` or replacement feature type modules
- `website/src/composables/useConfigWorkbench.ts`
- `website/src/components/ConfigWorkbench.vue`
- `website/src/components/ConfigHero.vue`
- `website/src/components/ConnectionTableCard.vue`
- `website/src/components/EditorSheet.vue`
- `website/src/index.css`
- `Doc/cli.md`
- generated `website/dist/**`

### Likely New Files

- `src/DatabaseMcpServer/Cli/CliToolInvoker.cs`
- `src/DatabaseMcpServer/Web/CliWebToolService.cs`
- `src/DatabaseMcpServer/Web/CliConnectionStringProfileCatalog.cs`
- `src/DatabaseMcpServer/Web/CliConnectionStringBuilder.cs`
- `tests/DatabaseMcpServer.Tests/CliConnectionStringBuilderTests.cs`
- `tests/DatabaseMcpServer.Tests/CliWebToolServiceTests.cs`
- `website/src/api/http.ts`
- `website/src/types/connections.ts`
- `website/src/types/tools.ts`
- `website/src/composables/useConnectionTable.ts`
- `website/src/composables/useConnectionHealth.ts`
- `website/src/composables/useConnectionStringWizard.ts`
- `website/src/composables/useTheme.ts`
- `website/src/composables/useToolPlayground.ts`
- `website/src/components/app/AppHeader.vue`
- `website/src/components/app/ThemeMenu.vue`
- `website/src/components/connections/ConnectionToolbar.vue`
- `website/src/components/connections/ConnectionDataTable.vue`
- `website/src/components/connections/ConnectionHealthSummary.vue`
- `website/src/components/connection-editor/ConnectionStringWizard.vue`
- `website/src/components/connection-editor/ConnectionFieldControl.vue`
- `website/src/components/playground/ToolPlayground.vue`
- `website/src/components/playground/ToolCatalogPanel.vue`
- `website/src/components/playground/ToolParameterForm.vue`
- `website/src/components/playground/ToolResultPanel.vue`
- `website/src/components/playground/DangerousToolDialog.vue`
- focused frontend test files alongside their features or under `website/src/**/__tests__/`

The exact split may be reduced if two proposed files remain genuinely small, but `App.vue`, `ConfigWorkbench.vue`, `EditorSheet.vue`, and `useConfigWorkbench.ts` must not absorb all five features.

## Validation Strategy

Run from `D:\Demo\my-mcp\DatabaseMcpServer` unless noted.

### Automated

```powershell
npm --prefix 'website' run test -- --run
npm --prefix 'website' run build
dotnet build 'DatabaseMcpServer.slnx'
dotnet test 'DatabaseMcpServer.slnx'
powershell -ExecutionPolicy 'Bypass' -File 'scripts/verify.ps1'
```

### Source CLI Regression

Use source execution so the result cannot come from a stale global tool:

```powershell
dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -f 'net10.0' -- tool list
dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -f 'net10.0' -- tool help 'execute_command'
```

Verify that tool names/options are unchanged and protected tools still report confirmation requirements.

### Local Web Smoke Test

Use an isolated temporary config and SQLite file. Do not point this smoke test at a business database.

```powershell
$dbmcpWebConfig = Join-Path $env:TEMP 'dbmcp-web-operations-test.json'
dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -f 'net10.0' -- -web --config $dbmcpWebConfig --port '5073' --no-browser
```

Validate manually/with Playwright:

- connections can be searched, type/status filtered, and sorted without losing selection;
- all-health check shows aggregate and per-row state and does not run automatically;
- wizard and raw modes both create a temporary SQLite connection;
- edit mode never exposes the stored SQLite/password value beyond the existing masked contract;
- light, dark, and system modes survive reload without unreadable controls or FOUC;
- catalog and forms are generated from server metadata;
- a read-only tool executes against the temporary SQLite database;
- a protected tool is rejected without exact confirmation and succeeds only against a uniquely named temporary object after confirmation;
- tool arguments/results disappear when cleared or when leaving the workspace;
- desktop `1440x900` and mobile `390x844` views have no overlap, clipping, or inaccessible controls.

### Packaging

```powershell
dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c 'Release'
```

Inspect the package to confirm the rebuilt `website/dist` assets and updated docs are embedded.

## Rollback Notes

- No persistent schema/data migration is introduced. Existing `databases.json` and `cli-state.json` remain valid throughout.
- Raw connection-string editing stays available, so wizard-specific code can be disabled without blocking configuration management.
- Health data and Playground state are memory-only; removing those endpoints/components leaves no cleanup data.
- Existing API request shapes remain supported; rollback can remove only the additive structured fields/endpoints and rebuild `website/dist`.
- Preserve the original `CliRunner` behavior while extracting the invoker. If CLI regression tests fail, revert the extraction first and keep Playground disabled rather than shipping divergent CLI/Web binding rules.
- If Playground security validation cannot be proven, omit the Playground workspace from the release rather than weakening confirmation, loopback, origin, or catalog restrictions.

## Completion Criteria

- All five requested features are usable end to end.
- Existing connection CRUD, preset, import/export, validate/doctor, current/default switching, and single-connection testing still pass.
- No endpoint or UI path reveals stored raw credentials.
- CLI and Web invoke the same catalog/binder semantics and dangerous-tool classification.
- Automated tests, Web build, solution build/test, verification script, package build, and desktop/mobile light/dark visual checks all pass.
