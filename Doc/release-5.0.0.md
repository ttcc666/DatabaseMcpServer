# DatabaseMcpServer 5.0.0 变更与迁移

更新日期：2026-11-26。本文汇总从 4.0.0 升级到 5.0.0 的变化。项目、MCP 清单和安装示例统一为 5.0.0。安装版请以 `DatabaseMcpServer --version` 和 `tool list/help --json` 为准。

## 主要变化

- 静态目录由 4.0.0 的 62 项调整为 54 项，MCP、CLI、Web 按配置 provider 和权限并集筛选；analysis 仅开放分析工具。
- 新增对象列表、对象定义和估算执行计划；表结构支持按 section 和批量读取，表分页支持主键 cursor 或带选列/筛选/排序的 offset 分页。
- 新增 `insert_row`、`drop_index`；创建索引和主键支持多列。单行插入首版支持 SQL Server、MySQL、SQLite 的整数自增列，ID 以十进制字符串返回。
- MongoDB 统一使用 SqlSugar.MongoDbCore 内置 ADO，提供六个集合工具，移除独立 MongoClient 注册表和 MongoDB.Driver 直接包引用。
- 新增 SQLite/PostgreSQL/SQL Server 原子 DML 批次和 CLI CSV/JSONL 流式导出，补充结果未知与部分成功状态。
- MCP 新增三类 Resources、两个 Prompts、连接名补全、ServerInstructions 和健康检查进度通知；这些不计入工具数。
- 默认查询预算为 1000 行、1 MiB、30 秒，新增应用级并发/排队限制，完善请求追踪、取消清理和元数据缓存指标。
- Host、Core、Infrastructure、测试与基准统一目标 .NET 10。Npgsql 显式固定为 8.0.9；兼容数据库产品按各自能力声明开放工具。

## SQL 调用迁移

| 旧调用 | 当前调用 |
| --- | --- |
| test_connection_by_name / test_connection_with_retry | test_connection，传 databaseName；重试需显式设置 maxRetries |
| get_table_info_list / get_view_info_list / get_proc_list / get_func_list / get_trigger_names | list_objects，使用 kind 和 nextCursor |
| get_column_infos_by_table_name / get_primaries / get_is_identities / get_index_list | get_table_schema，按 sections 读取 |
| sql_query_single / get_scalar | query_database；首行 maxRows=1，聚合列命名，检查 data 数组及截断 |
| batch_sql_query 的 queries | commands: [{sql, parameters?}]，最多 5 项 |
| DDL 的 SqlSugar DbColumnInfo 字段 | columns/column 自有 DTO，使用 name/dataType/nullable 等字段 |

旧入口不保留别名。刷新 Agent 提示词和工具缓存；旧 cursor 从第一页重新获取。表分页指定 columns、filters 或 orderBy 后采用 page 页码，返回 nextCursor=null，不能混用 cursor。该分支整页物化后检查响应字节预算，不限制读取峰值内存；未指定新选项时继续使用原主键 cursor 路径。

insert_row 要求普通表名和唯一可识别的整数自增列，SQLite 必须使用 SqlSugar 可识别的 AUTOINCREMENT 定义。row 为 1–128 个字段、最多 4 MiB 的对象，字段区分大小写，值仅 string/bool/number/null，不得包含自增列。PostgreSQL、schema 限定表名、复杂类型和显式参数类型使用 execute_command。取消不保证回滚，不能自动重试插入。

create_index 与 add_primary_key 的 columnName/columnNames 二选一，数组为 1–32 列；执行仍受 provider 能力和 schema-admin 权限限制。

## MongoDB 调用迁移

| 变化 | 调整方式 |
| --- | --- |
| mongo_insert_one 删除 | 使用 mongo_insert_many，单条也传 documents: [document]；结果为 insertedCount，不再返回 insertedId |
| mongo_update / mongo_delete 改为批量 | 移除 many/upsert；检查筛选范围，启用 enableDangerousOperations；空 filter 另需 allowAll=true |
| mongo_truncate 新增 | 清空文档并保留集合/索引；要求 schema-admin 和危险操作开关 |
| mongo_list_collections / mongo_count 删除 | 使用已知集合；统计可通过只读 mongo_aggregate 的 $count stage 完成，空输入可能返回空 documents |
| mongo_collection_info / mongo_explain 删除 | 这两个入口仅曾存在于开发版本，当前不开放；集合元数据/计划需使用数据库原生管理工具 |
| mongodb+srv URI 不支持 | 改用服务提供方给出的显式主机 mongodb URI，必须包含业务数据库路径 |
| 输出不再是无损 BSON | 顶层 ObjectId 为字符串、Int64 为 JSON 数字、缺失字段与显式 null 合并；查询 ObjectId 需显式 $oid |

输出 Decimal128 受 CLR decimal 范围约束，二进制输出 Base64 且 subtype 丢失；嵌套 BSON 保留部分使用 Extended JSON。provider 先物化有行数限制的结果，再检查响应字节；初始同步查询可能延迟取消，执行槽保持到清理结束。内置命令不设置 MaxTime/allowDiskUse。writeOutcome=unknown 或取消后，先核实数据，禁止自动重放。完整边界见 [MongoDB 集合契约](mongodb-collections.md)。

## MCP 协议扩展

| 类型 | 入口 |
| --- | --- |
| Resources | db://connections、db://capabilities/{databaseName}、db://schema/{databaseName}/{tableName} |
| Prompts | analyze_slow_query(sql, databaseName?)、safe_schema_migration(tableName, changeGoal, databaseName?) |
| Completion | databaseName/database_name 前缀匹配，最多返回 100 项并报告 total/hasMore |
| Progress | health_check 按连接报告已完成数，需客户端请求进度通知 |

Prompt 只返回指导步骤，不自动执行 SQL 或修改 schema。资源复用现有连接、能力和结构读取操作。

## 发布验证

这些变更包含工具删除和语义变化，因此采用主版本号 5.0.0。发布前核对 PackageVersion、MCP 清单的包版本/顶层版本、安装示例、README 版本徽章和发布说明均为 5.0.0；完成验证并提交最终变更后，再为该提交创建同名标签。版本号更新不表示已完成发布验收。已发布的 4.0.0 不可覆盖；`--skip-duplicate` 不会更新旧包。

2026-11-26 本地验证结果：Release 非集成测试 833 项、真实 SQL 数据库集成测试 101 项、MongoDB 集成测试 8 项全部通过且无跳过；前端 16 项测试通过。54 个 CLI 入口完成检查（包含预期的权限/provider 拒绝路径），工具清单和能力文档与实现一致。NuGet 包安装、CLI/stdio、DuckDB 原生读写及缺失扩展隔离冒烟测试通过；依赖审计未发现已知漏洞。

在仓库根目录执行以下命令复验；完整 CI 结果以对应提交的 GitHub Actions 为准：

```powershell
pwsh -NoProfile -File 'scripts/verify.ps1'
dotnet test 'tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj' --configuration Release --framework net10.0 --filter 'Category!=Integration&Category!=MongoIntegration'
pwsh -NoProfile -File 'scripts/update-tool-manifest.ps1' -Check
pwsh -NoProfile -File 'scripts/verify-cli-tools.ps1' -Framework net10.0 -Configuration Release
pwsh -NoProfile -File 'scripts/test-integration.ps1' -Framework net10.0 -Configuration Release
pwsh -NoProfile -File 'scripts/test-mongodb-integration.ps1' -Framework net10.0 -Configuration Release
pwsh -NoProfile -File 'scripts/test-package.ps1'
dotnet list 'DatabaseMcpServer.slnx' package --vulnerable --include-transitive --no-restore
Push-Location website
npm test
Pop-Location
```

完整 CI 还检查 Linux 入口、配置监听和前端。旧版文档中的测试数量只代表相应日期和代码状态。发布流程见 [Trusted Publishing](trusted-publishing.md)，调用参数见[工具手册](../TOOLS.md)。
