# 扩展数据库优化策略指南

本文对应当前 Host / Core / Infrastructure 结构，说明如何增加或调整数据库适配。SqlSugar 支持一项功能不代表 MCP 工具已开放；配置含义见 [ORM 配置约定](orm-configuration.md)，产品能力见[能力参考](capabilities.generated.md)。

## 代码入口

所有驱动相关实现位于 src/DatabaseMcpServer.Infrastructure，Core 保持无 NuGet 依赖。现有命名空间 DatabaseMcpServer.Strategies.DBSetting 不代表旧的物理 Strategies 目录。

| 路径（相对 Infrastructure） | 职责 |
| --- | --- |
| Providers/IDatabaseOptimizationStrategy.cs | ApplyOptimizations(ConnMoreSettings, Dictionary<string,string>?) 与 GetDescription 契约 |
| Providers/{Provider}/{Provider}OptimizationStrategy.cs | 当前 provider 的 ConnMoreSettings 映射；参考 PostgreSql/PostgreSqlOptimizationStrategy.cs |
| Providers/DatabaseOptimizationStrategyFactory.cs | DbType 到策略实例的映射 |
| Providers/DatabaseOptimizationOptions.cs | 已知配置键、值校验、mapped/advisory/not-applicable 及诊断范围 |
| Providers/DatabaseProviderCatalog.cs | 产品名解析、实际驱动映射、程序集要求、离线连接串验证与产品验证状态 |
| Providers/DatabasePresetCatalog.cs | CLI/Web 共用预设 |
| Providers/ProviderCapabilities.cs | 工具能力、schema section 和操作支持范围 |
| Sql/Clients/SqlSugarConnectionSettings.cs | 组装执行与离线诊断共用的 SqlSugar 设置 |
| Sql/Clients/SqlSugarClientFactory.cs | 每次操作的独占 SqlSugarClient、AOP 与释放 |
| Sql/Clients/SqlSugarProviderWarmup.cs | 扩展程序集注册 |

Host 通过构造器注入调用 Infrastructure。新增 provider 不得让 Infrastructure 反向依赖 MCP SDK，也不得绕过租约创建共享 SqlSugarScope 或新的原生客户端缓存。

## 实施步骤

1. 确认数据库产品与实际驱动。兼容 MySQL/PostgreSQL 协议的产品仍保留自身能力和验证状态；不要仅因实际驱动相同就开放分页、执行计划、存储过程或事务工具。
2. 在 Providers/{Provider}/ 中实现或修改策略，只映射当前依赖实际支持的 ConnMoreSettings。沿用现有构造器和日志方式，新增映射注册到 DatabaseOptimizationStrategyFactory。
3. 在 DatabaseOptimizationOptions 定义可接受键、类型、范围和作用范围，保证执行和离线能力报告一致。已知非法配置在边界拒绝；未知键按既有规则保留并隐藏值。
4. 更新 DatabaseProviderCatalog 的解析/映射/驱动要求及连接串校验，更新 DatabasePresetCatalog 的可用示例。需要新增生产依赖时先按仓库约定确认，再核对版本、资产及平台要求。
5. 根据已实现的最小契约调整 ProviderCapabilities。仅列出确实可用的能力；provider-dependent/unverified 与 supported/integration-tested 不可混用。
6. 若新增 MCP 工具，在 Host 的 Tools 对应功能目录定义输入、输出、ToolPolicy 和描述，并在 DatabaseMcpToolCatalog 注册必要的工具类。现有工具增加 provider 时通常只需要更新能力声明与执行路径。
7. 补充对应行为验证、配置文档和迁移说明，再由生成脚本更新能力参考及 .mcp/server.json。

## 配置映射边界

- 连接池、TLS、Schema、字符集等驱动选项应写入连接字符串；不能只记录日志便宣称优化已经生效。
- NoLock、表名大小写、ILike 等 ORM 设置具有明确作用范围，不自动改写原始 ADO SQL。带选项的表分页使用 Queryable，也需按当前实际路径验证。
- enableBulkCopy 等建议项没有对应 MCP BulkCopy 工具；insert_row 是单行整数自增插入，mongo_insert_many 是文档数组写入，不应混同。
- Mongo 只通过 SqlSugar.MongoDbCore 内置 ADO，使用现有 MongoCollectionService；输入/输出转换、同步取消限制和 SRV 拒绝策略见 [Mongo 契约](mongodb-collections.md)。
- 日志和离线诊断只输出允许公开的设置，不输出连接串、密码或文档值。默认值、错误处理和权限检查复用现有实现。

## 验证与文档

在 tests/DatabaseMcpServer.Tests/Providers 中验证设置映射、非法输入、能力声明与执行路径的一致性。需要实库证明的行为放入 Integration/Sql 或 Integration/Mongo，说明所需环境；不能把程序集可加载或配置可解析写成实库验收通过。

在仓库根目录构建后生成和检查文档：

```powershell
dotnet build 'DatabaseMcpServer.slnx' --configuration Release
pwsh -NoProfile -File 'scripts/update-tool-manifest.ps1'
pwsh -NoProfile -File 'scripts/update-tool-manifest.ps1' -Check
```

脚本默认使用 Release/net10.0 程序集；生成的 capabilities.generated.md 不手工修改。实际测试和包验证命令见[发布检查](release-5.0.0.md#发布前检查)。同时更新 DatabaseSetting/{Provider}.md、README 支持范围和工具迁移说明，明确依赖可用性、功能边界及未验证的部署环境。

最后更新：2026-11-26。
