# 后端模块化迁移

基线：`ea9f3c5`。用户已确认按架构分析中的四个批次实施。本次保持一个进程、一个工具包，由当前会话直接完成，不使用子代理。

## 范围与不变量

- 保留 62 个 MCP 工具的名称、参数、JSON 字段、错误码、CLI 退出语义及配置格式；不增加旧工具兼容层。
- 排队前捕获目标、权限与配置版本；连接重命名保持准入身份；取消和释放不会泄漏额度。
- 保留 SQL 独立操作客户端、Mongo 客户端复用、查询行/字节预算和原生类型精度。
- 写入确认、危险操作保护、部分成功、Mongo 写入结果未知的语义保持不变。
- 配置原子保存、运行时发布和当前选择持久化分别报告状态。
- 源码与测试保持 net9.0/net10.0；发布仅 net10.0；不升级依赖或包版本。

## 实施清单

- [x] 基线：双框架构建、非远程集成回归，记录工具 schema。
- [x] 批次 1：应用结果/错误与 MCP 输出分离；SQL/Mongo 共用错误结果映射，保留预编码有界结果。
- [x] 批次 2：抽出与驱动无关的连接快照及执行准入；SQL 和 Mongo 分别取得各自客户端。
- [x] 批次 3：配置存储、应用操作和运行状态分开；CLI/Web 直接使用类型化配置结果；工具描述及数据库元数据归属统一。
- [x] 批次 4：Host/Core/Infrastructure 三个生产项目；单向引用；前端资产构建显式化；补充边界检查。
- [x] 最终验证：双框架构建/回归、SQL 和 Mongo 容器测试、62 工具协议/CLI、net10 包安装、文档与差异复核。

## 目标边界

Host 负责 MCP、CLI、Web 与依赖组装。Core 负责中立契约、连接快照、权限、准入、配置状态与结果。Infrastructure 负责 SqlSugar、Mongo、文件持久化和数据库专属实现。Core 不引用 Host 或数据库驱动，Infrastructure 不引用 Host。MCP SDK 仅用于 Host。

保留原命名空间时以物理项目/程序集依赖强制边界；避免无必要的 API 改名、泛型仓储和插件框架。按真正的职责拆分，不为每个方法增加转发接口。

## 验证策略

沿用 xUnit/VSTest。每批先运行受影响测试；跨程序集迁移后运行完整双框架回归。新测试重点验证程序集依赖、原生 Mongo 不需 SQL 客户端、结果适配与配置部分成功。以现有真实数据库集成和包安装测试覆盖运行时反射、资源及驱动资产。

## 验收记录（2026-11-20）

| 检查 | 结果 | 本地证据 |
| --- | --- | --- |
| Release 双框架构建 | 0 warnings / 0 errors | `artifacts/architecture-refactor/build-final.log` |
| 非容器回归 | net9 / net10 各 449 passed，0 failed / skipped；基线各 435 | `artifacts/architecture-refactor/tests-final.log` |
| SQL 容器集成 | MySQL 8.4、PostgreSQL 17、SQL Server 2022；每个框架 38 passed | `artifacts/architecture-refactor/sql-integration.log` |
| Mongo 容器集成 | MongoDB 8.0；每个框架 10 passed | `artifacts/architecture-refactor/mongo-integration.log` |
| 工具契约比较 | 62 个声明一致，27 项确认规则一致 | `artifacts/architecture-refactor/contract-comparison.json` |
| CLI 全工具调用 | 62 / 62 覆盖；43 项退出成功，19 项为 SQLite 不支持操作或拒绝 Mongo 连接类型的既有错误 | `artifacts/architecture-refactor/cli-tools.log` |
| net10 工具包 | 149.2 MiB；临时目录安装、CLI / stdio、DuckDB 原生读写、缺失 HG 扩展隔离通过 | `artifacts/architecture-refactor/package-final.log` |
| 前端资产构建 | `npm --prefix 'website' run build` 通过，产物无内容变更；仍有现有大 chunk 提示 | `artifacts/architecture-refactor/website-build.log` |
| 包元数据回归 | 复现双框架 assets 配合 `--no-restore` 导致 NU5128；独立构建目录修复，共用检查拒绝错误包、接受正常包 | `scripts/verify-package-content.ps1`、`artifacts/architecture-refactor/pack-dualassets.log` |
| 测试有效性 | 临时反转可选 database 字段条件，配置失败测试按预期失败；原始字节恢复后完整回归通过 | `artifacts/architecture-refactor/mutation.log` |

新增 14 个用例覆盖程序集依赖、Mongo 无 SQL 工厂、离线能力查询无客户端构造、配置失败输出、序列化错误和 Web JSON 类型绑定。重点用例：`Assemblies_EnforceCoreAndInfrastructureDependencyDirection`、`MongoExecution_ReachesNativeRegistryWithoutSqlFactory`、`ConfigurationEditFailure_PreservesOmittedOptionalFieldsAndSavedStatus`、`JsonArguments_KeepExtendedJsonDocumentsAfterSourceDisposed`。

容器测试已清理自身容器，保留原有 `searxng-core` / `searxng-valkey`。未扩展尚未实测数据库的验证承诺，也未测量新的吞吐指标。未修改依赖版本，未提交或推送。

## 回退

每批保持可构建并记录验收。失败时修复或撤销该批自身修改，保留此前用户工作。未获明确指示不提交、不推送，不改写 `ea9f3c5`。当前基线及 Git 差异用于逐批复核；测试报告放入忽略的 artifacts 目录。
