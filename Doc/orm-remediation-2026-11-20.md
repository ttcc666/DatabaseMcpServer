# ORM 审计修复记录

日期：2026-11-20。基于 [原始审计](orm-provider-audit-2026-11-20.md)，实施计划见 [计划](../plans/2026-11-20-orm-audit-remediation.md)。配置契约见 [ORM 配置约定](orm-configuration.md)。

后续已补齐 HG/GBase/TDengine/DuckDB 扩展，版本、原生客户端要求及新增验证见[驱动补齐记录](provider-drivers.md)。下文保留本轮修复时的测试结果。

MongoDB 后续已实现[集合查询/写入契约](mongodb-collections.md)，下文 Mongo 未开放的描述仅代表该审计阶段。

## 已修复

- 元数据工具统一禁用 SqlSugar 反射缓存；主键/自增从新鲜列信息派生，视图和列存在性在 DDL 后可见最新结果。修正表/视图/主键说明。
- 进入 SqlSugar 前检查扩展依赖，按官方 CustomAssemblies 集中注册已安装扩展。HG/GBase/TDengine/DuckDB 不可用时明确报错；正常连接仍可继续使用。
- 修正 Kdbndp User ID 预设，CLI/Web 预设公开 availability/unavailableReason，连接串敏感值不进入校验错误。
- optimizationSettings 支持字符串/布尔/数字标量，规范写回字符串；验证已知选项值和向导端口范围，保留未知键。
- 区分数据库产品、实际 provider、工具可用性和验证程度。兼容驱动不能继承产品实库验证；过程输出仅开放 MySQL/SQLServer。
- Gauss 驱动只由 dbType 决定；原生 databaseModel 与旧 isOpenGauss 模式别名统一，冲突时报错。nativeDriver 作为旧建议键保留。
- 策略仅接收真实映射选项，移除静默吞掉策略错误的默认回退；建议/未实现/未知项统一诊断，避免误报“已应用”。删除未注册的 DB2/HANA 策略。
- MongoDB 通用 SQL 入口明确拒绝未实现的契约；同步元数据/DDL 纳入超时预算，不宣称支持请求取消。
- 重写 Gauss/Kdb 配置文档，纠正 NoLock 与 Mongo 工具边界、支持清单和包说明，去掉无测量依据的性能数字。

## 回归证据

新增测试在 `tests/DatabaseMcpServer.Tests/OrmProviderRegressionTests.cs`。

| Requirement | Evidence |
| --- | --- |
| 元数据隔离和 DDL 刷新 | MetadataTools_IsolateSameNamedTablesAndRefreshAfterDdl |
| 同步超时上限 | SynchronousMetadataAndDdl_RespectServerTimeout |
| 24 个预设离线构造/明确缺包 | Presets_ConstructOfflineOrExplicitlyReportMissingDependency |
| 缺包不污染后续 provider 或停用正常连接 | MissingExtension_DoesNotPoisonInstalledProvider；MissingExtensionConfiguration_DoesNotDisableOtherConnections |
| 配置标量、非法值及脱敏 | Configuration_ReadsScalarOptionsAndRejectsInvalidValuesWithoutSecrets；Configuration_RejectsNonScalarOptions |
| 向导端口边界与金仓用户名 | Wizard_RejectsInvalidRelationalPorts；Wizard_AcceptsPortBoundariesAndUsesKdbUserId |
| 优化项真实作用范围 | OptimizationDiagnostics_ReportMappedValuesAndUnimplementedOptions |
| Gauss 驱动/模式与配置兼容 | GaussDriver_IsSelectedByDbTypeOnly；GaussModel_PreservesLegacyOptionAndRejectsConflicts |
| 产品与驱动能力分离 | Capabilities_SeparateProductVerificationFromDriverAvailability；TiDbProcedureOutput_IsRejectedBeforeOpeningConnection |
| Mongo SQL 入口在打开连接前拒绝 | MongoSqlTools_ReturnUnsupportedBeforeOpeningConnection |

在仓库根目录执行：

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'Category!=Integration'
pwsh -NoProfile -File 'scripts/test-integration.ps1'
pwsh -NoProfile -File 'scripts/test-package.ps1'
pwsh -NoProfile -File 'scripts/verify-cli-tools.ps1' -Framework 'net10.0' -Configuration 'Release'
```

验证结果：

- net9.0 / net10.0 非集成测试各 Passed: 403, Failed: 0, Skipped: 0（原 357 + 新增 46）。最终优化项脱敏调整后，两框架相关子集各 Passed: 50, Failed: 0, Skipped: 0。
- MySQL 8.4、PostgreSQL 17、SQL Server 2022 隔离容器集成：两框架各 Passed: 38, Failed: 0, Skipped: 0。脚本成功移除本轮容器，原 searxng 容器未操作。
- NuGet 打包、临时 tool-path 安装、CLI 退出码和 MCP stdio 冒烟通过；发现 56 个工具，验证缺包错误后 Mongo provider 仍能诊断、Mongo 查询被拒绝、SQLite 查询继续正常。
- CLI 覆盖 56/56 工具：43 个退出码 0；13 个 SQLite 不支持的操作按预期返回错误，与既有基线一致。报告：`artifacts/cli-tool-verify/20261120-092021/cli-tool-summary.md`。
- Gauss/Kdb 文档的三个完整 JSON 配置经实际 CLI `config validate` 全部通过。
- `git diff --check` 通过；既有暂存文件仍为 65 个，本轮变更未暂存、未提交。

人工复核检查了参数绑定、权限、超时恢复、配置兼容、无远程连接的预检和诊断脱敏。缺失 provider 采用配置 warning + 调用时拒绝，避免因一个可选连接缺包而阻断整份多库配置。

## 未扩展的范围

未引入缺失可选包或原生 SDK；未实现 Mongo 集合工具、BulkCopy、原生数组/游标参数。其他国产数据库尚无实库验收。SqlSugar SQLite 自增检测的大写 AUTOINCREMENT 限制已记录，未引入自定义 DDL 解析。未改变依赖版本、暂存区和已有提交。
