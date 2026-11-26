# CLAUDE.md

本文件说明当前仓库的开发入口；工程约定以 [AGENTS.md](AGENTS.md) 为准。当前源码对应[5.0.0](Doc/release-5.0.0.md)，静态目录有 54 项工具，实际发现结果按配置 provider、权限和 full/analysis profile 筛选。

## 构建和运行

源码、测试、基准及工具包统一目标 .NET 10，SDK 版本由 global.json 指定。在仓库根目录运行：

```powershell
dotnet build 'DatabaseMcpServer.slnx'
dotnet test 'tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj' --configuration Release --framework net10.0 --filter 'Category!=Integration&Category!=MongoIntegration'
$env:DB_CONFIG_PATH = 'D:/config/databases.json'
dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' --framework net10.0
```

普通后端构建使用已跟踪的 website/dist；更新 UI 或发布打包时显式启用 BuildWebsite：

```powershell
dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c Release '-p:BuildWebsite=true' --artifacts-path 'artifacts/package-build'
pwsh -NoProfile -File 'scripts/update-tool-manifest.ps1' -Check
DatabaseMcpServer --version
DatabaseMcpServer tool list --json --all
```

清单检查默认读取 Release 构建。SQL、Mongo 实库和安装包验收使用 scripts/test-integration.ps1、test-mongodb-integration.ps1、test-package.ps1；需要的环境和完整顺序见[发布检查](Doc/release-5.0.0.md#发布前检查)。dotnet pack 只生成包，不发布。

## 项目和目录边界

| 项目 | 职责 |
| --- | --- |
| src/DatabaseMcpServer | Host：Program、Hosting、Extensions、Tools、Resources、Prompts、Filters、Serialization、Cli、Web |
| src/DatabaseMcpServer.Core | 无 NuGet 依赖的配置/连接/执行/SQL/Mongo/Schema/结果契约与策略 |
| src/DatabaseMcpServer.Infrastructure | 配置持久化、驱动和应用操作；只引用 Core，不引用 Host 或 MCP SDK |
| tests/DatabaseMcpServer.Tests | xUnit，按功能分组；Support 共享夹具，Integration/Sql 与 Integration/Mongo 为实库测试 |
| website | 可选 Vue 配置 UI |

工具物理路径为 Tools/Connections、Tools/Schema、Tools/Sql、Tools/Mongo；没有旧的 Export/Documentation 工具模块。CSV/JSONL 导出是 CLI 顶级 export。新增类型放入所属功能模块，不重建项目级 Models/Interfaces/Helpers/Services/Operations 文件夹；历史命名空间与物理目录可能不同。

## 启动和执行

1. Program.cs 解析启动参数，无参数启动 stdio MCP；CLI、Web、配置和导出由 CliRunner 路由。机器结果使用 stdout，日志和普通帮助使用 stderr。
2. Extensions/ServiceCollectionExtensions.cs 注册应用服务、9 个工具类、Resources 和 Prompts。连接工厂与配置状态为共享服务，操作和工具按各自 DI 生命周期注册，并非全部 singleton。
3. ToolCatalog 提供 MCP/CLI/Web 共同元数据，ToolExposurePolicy 过滤目录和调用，ToolInputSchema 与 ToolInvoker 处理输入契约；ToolPolicyAttribute 声明能力、权限和确认要求。
4. 配置在排队前捕获目标、权限和版本。DatabaseExecutionCoordinator 联合控制全局与单连接额度，SqlDatabaseSessions / SqlSugarClientFactory 为操作提供独占客户端租约。
5. 异步 SQL 通过 SqlRequestExecutor 统一总 deadline、错误、遥测与写后缓存失效；原子事务有独立 commit/rollback 生命周期，同步 GO/DDL 受驱动限制。
6. MongoCollectionService 使用 SqlSugar.MongoDbCore 内置 ADO，共用 SQL 工厂/准入；没有独立原生 MongoClient 注册表。探活使用有限 aggregate，读取物化后检查字节预算，初始同步请求可能延迟取消。
7. McpResultAdapter 输出 text/structuredContent，失败设置 isError；客户端清理完成后释放执行额度，取消不保证写入回滚。

表查询选项、单行插入、Mongo 六工具和 MCP Resources/Prompts 的完整契约见 [TOOLS.md](TOOLS.md)，生命周期见[后端架构](Doc/backend-architecture.md)。

## 配置和权限

| 项目 | 规则 |
| --- | --- |
| MCP stdio | DB_CONFIG_PATH；未设置时读取用户目录/.database-mcp/databases.json |
| tool / -web | --config → 当前目录 databases.json → local-databases.json → DB_CONFIG_PATH → 用户目录配置 |
| init / config | --config，默认用户目录配置 |
| 单次选库 | databaseName / --database-name 固定本次目标，不切换全局当前库 |
| DB_TOOL_ACCESS / accessMode | read-only、read-write、schema-admin，取更严格者 |
| enableDangerousOperations | 独立的危险 SQL/Mongo 批量写保护；CLI --yes 不覆盖权限 |
| ENABLE_MONITOR_CONFIG | 启动参数优先于环境变量，再到配置根字段，默认关闭 |
| SEQ_SERVER_URL / SEQ_API_KEY | 可选日志目标与凭据 |

DB_CONNECTION_STRING/DB_TYPE 旧启动方式已拒绝。Mongo URI 必须指定业务库，当前不支持 SRV。原始连接串、文档和敏感参数不得写入日志；使用既有脱敏与错误映射。部署仍应使用最小权限数据库账号，SQL 校验不等同数据库授权。

默认每连接并发 8/排队 32，全局并发 32/排队 128；默认查询预算 1000 行、1 MiB、30 秒。驱动返回和清理可能晚于 deadline，不能提前释放租约；写入不自动重试。

## 扩展数据库能力

- 在 Infrastructure/Providers/{Provider}/ 添加或更新策略，维护 DatabaseOptimizationStrategyFactory。
- 在 DatabaseProviderCatalog、DatabasePresetCatalog 和 DatabaseOptimizationOptions 中维护产品解析、实际驱动、预设和配置映射。
- 在 ProviderCapabilities 声明实际能力；兼容驱动不自动继承产品验收。
- 如需外部扩展，核对 SqlSugarProviderWarmup 和包内资产；保持 Core 无驱动依赖。
- 同步测试、DatabaseSetting 文档、能力参考和工具清单。详见[扩展指南](Doc/extending-database-optimization.md)。

## 代码和提交约定

使用 nullable、implicit usings、file-scoped namespace、四空格和次行大括号；公共符号 PascalCase，私有 readonly 字段 _camelCase。依赖通过构造器注入，文本使用 UTF-8 无 BOM。遵循 AGENTS.md 的目录和验证规则，不提交凭据。仅在用户明确要求时提交，保留无关工作区及暂存修改；提交说明描述行为影响和实际验证。
