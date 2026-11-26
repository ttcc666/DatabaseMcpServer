# MCP / CLI 工具精简与扩展

本文描述 5.0.0 的工具契约；发布范围与升级清单见[5.0.0 说明](release-5.0.0.md)，安装版本以实际 tool list/help 为准。

普通查询、非事务命令/批次、分页、schema、对象元数据、导出和 explain 共用 `SqlRequestExecutor`。异步默认总超时为服务端查询配置（通常 30 秒），包含排队、执行和读取，非原子批次共享一次计时。原子事务和同步 GO/DDL 保留独立生命周期。

完整 CLI 目录的 `capabilityCatalog`、执行检查、离线报告与[能力参考](capabilities.generated.md)共用能力声明。`update-tool-manifest.ps1 -Check` 同时校验工具清单和能力文档。

静态总目录为 **54 项**，新增 `drop_index` 和 `insert_row`，Mongo 工具按 SqlSugar 集成能力收敛为 6 项。MCP、CLI 和 Web 的 `full` 与 `analysis` 都按配置连接的 provider 和权限并集筛选。`tool list --json --all` 展示静态契约。

SQLite 配置隐藏 11 个当前 SqlSugar 适配不支持的入口，包括 `get_data_base_list`、`is_any_constraint`、`rename_column`、`add_primary_key`、`drop_constraint`、`add_default_value` 以及五个表/列备注入口。混合配置可能仍列出这些入口，目标为 SQLite 时返回 1012。

`tool list/help --json` 及 Web 目录列出 `supportedProviders` 和 `unsupportedProviders`。`get_database_capabilities` 离线返回 schema、架构操作和导出格式的能力，实际连通性用 `test_connection` 检查。`provider-dependent` 表示由实际驱动提供支持。

能力按配置的数据库产品判断，SQL 方言和参数绑定按实际驱动处理。TiDB/Vastbase 等兼容产品保留通用 SQL、对象列表和基础表结构，专用分页、原生定义、执行计划、存储过程和原子批次仍按显式能力开放。驱动映射见 `actualProvider`。

普通查询、未指定新增选项的表分页、存储过程和执行计划复用已经过预算检查的 UTF-8 JSON 行。带选列、筛选或排序的表分页使用 SqlSugar 官方分页 API，整页读取后检查响应字节预算。此前的代表性 reader 对照中，SQL Server、PostgreSQL 与 MySQL 的托管分配分别下降约 65%、85% 和 91%。结果仅适用于当时的测试负载。

PostgreSQL 驱动显式固定 Npgsql 8.0.9，替代 SqlSugar 间接引入的 5.0.18，修复连接池重置期间取消请求可能丢失的问题；对应上游 [#5191](https://github.com/npgsql/npgsql/issues/5191) / [#5774](https://github.com/npgsql/npgsql/pull/5774)。取消仍等待驱动结束再释放租赁，不延长调用超时或关闭连接池。SQLSugar 既有 timestamp 兼容设置保留，日期/时区参数、事务和元数据经过真实 PostgreSQL 回归。

MySQL / SQL Server / PostgreSQL / SQLite 的 analysis 目录为 9 项：`list_databases`、`get_database_capabilities`、`test_connection`、`list_objects`、`get_table_schema`、`get_object_definition`、`query_database`、`query_table_page`、`explain_query`。Mongo 目录为 5 项：三个公共连接工具加 `mongo_find`、`mongo_aggregate`。混合配置为 11 项。其他 SQL provider 使用 list_objects 的 provider 适配，暂不公开原生定义/计划工具。

```powershell
$env:DB_MCP_TOOL_PROFILE = 'analysis'
DatabaseMcpServer --version
DatabaseMcpServer tool list --json
DatabaseMcpServer tool help 'mongo_aggregate' --json
```

JSON list/help 写 stdout；普通帮助和驱动诊断写 stderr。JSON 目录包含 SDK inputSchema/outputSchema、camelCase 参数与 kebab-case 选项映射、权限和 provider。`.mcp/server.json` 由分支构建的 `scripts/update-tool-manifest.ps1` 更新，工具条目仅包含名称和描述，以满足 NuGet 的 20,000 字节读取上限；完整权限和 provider 元数据通过 CLI 目录与能力参考查询。`-Check` 检查名称和描述与当前目录一致。

## 旧入口如何收敛

从已发布的 4.0.0 升级时，更新 Agent 提示词和脚本中的旧工具名与参数。多结果及批量查询默认共享 1000 行、1 MiB 预算。调用方应检查 `limitReached`、`truncated` 和每项执行状态，按业务需要调整预算。

| 已删除入口 | 当前入口 | 调用方式 |
|---|---|---|
| test_connection_by_name / test_connection_with_retry | test_connection | databaseName 指定目标，maxRetries 显式启用重试，默认零重试 |
| get_table_info_list / get_view_info_list / get_proc_list / get_func_list / get_trigger_names | list_objects | kind 选择类型，nextCursor 翻页；不能把第一页当全量 |
| get_column_infos_by_table_name / get_primaries / get_is_identities / get_index_list | get_table_schema sections | columns / primaryKeys / identityColumns / indexes，按需读取与缓存 |
| sql_query_single / get_scalar | query_database | 首行用 maxRows=1；聚合列显式命名；统一 data 数组，检查空结果/截断 |
| runtime 切库、reload、health、DDL/备注管理 | full / CLI / config | analysis 不暴露；权限校验仍独立执行 |

`get_data_base_list` 列数据库实例可见的数据库，`list_databases` 列本地配置连接，两者不同。`backup_table` 是 provider 的表复制能力，不是完整数据库备份。

## SQL 元数据与估算计划

`list_objects --kind table|view|procedure|function|trigger` 支持 schema、字面 name-prefix、父表、cursor 和 limit。过滤/分页在数据库端执行；cursor 绑定连接名称和类型、已应用配置内容与路径的稳定指纹、工作目录及查询条件，可跨 CLI 进程续页。配置或查询条件改变后需重新获取；升级前的令牌不兼容。可见性受权限影响，空结果不能证明对象不存在。SQLite 仅支持 main 的 table/view/trigger。

上述服务端过滤/分页适用于 MySQL、SQL Server、PostgreSQL、SQLite，返回 `pagingMode=keyset`；按 schema/kind/name/identity 严格排序并参数化续页，前页对象删除不会使下一页跳过未读项。其他 SQL provider 使用原有 SqlSugar 元数据能力，返回 `pagingMode=client`，在读取 provider 列表后筛选分页，不承诺数据库读取量/内存有界或异步取消。该模式不支持 schema 参数（schema 返回空字符串），trigger 必须提供 tableName；具体对象类型取决于驱动能力。DuckDB 已纳入回归，其余 provider 未实库验证。

`get_object_definition` 使用列表返回的 identity；PostgreSQL 重载例程包含参数签名，trigger 使用带必要标识符引用的 table.trigger，例如 `"a.b".c` 与 `a."b.c"`。请原样传回 identity，不自行拆分。SQL Server/PostgreSQL 不重建表 DDL。`definition-unavailable` 不猜测加密、权限或缺失原因；大定义超出预算时报错。

`get_table_schema` 默认读取 `columns`、`primaryKeys`、`identityColumns` 和 `indexes`。SQLite、MySQL、PostgreSQL、SQL Server 和 DuckDB 使用异步原生读取。其他 SQL provider 使用 SqlSugar 的同步元数据 API，表名需符合项目的安全标识符规则（ASCII 字母、数字、下划线、点和美元符号）。基础字段和索引取决于驱动，主键按驱动返回的列顺序报告，高级 section 返回 `unsupported`。同步适配在驱动返回后检查取消和预算，驱动可能先物化元数据。支持单表或 1–50 个不同表名，整批共享行数和字节预算，缓存命中仍计入预算。

`explain_query` 只接受单条 SELECT/WITH；SQL Server SHOWPLAN_XML、PostgreSQL EXPLAIN (FORMAT JSON, ANALYZE FALSE)、SQLite EXPLAIN QUERY PLAN、MySQL EXPLAIN FORMAT=JSON（不使用 ANALYZE）。执行计划保留原生格式，不统一虚构成本。SQL Server 以参数类型声明局部变量生成计划（`parameterMode=types-only`），不插入或执行参数值，不模拟 parameter sniffing；参数化 RPC 在 SHOWPLAN 模式下不执行内部查询，因此不能直接复用普通查询发送方式。当前支持标量参数，数组请改用多个标量参数。其他 provider 为 `bound-values`。`executed=false` 指未执行目标查询，不是函数规划期行为的安全保证；应使用数据库只读角色。失败时恢复 SHOWPLAN，恢复失败清理连接池中的该连接。

## Mongo

Mongo 通过 SqlSugar 的连接租约和内置 ADO 命令接入，没有自定义查询 handler、reader 或独立原生 MongoClient。保留 `mongo_find`、`mongo_aggregate`、`mongo_insert_many`、`mongo_update`、`mongo_delete`、`mongo_truncate`，迁移规则见 [Mongo 集合集成契约](mongodb-collections.md)。

聚合允许 1–64 个 stage、4 MiB 总输入，支持 match/project/group/sort/limit/skip/unwind/count/addFields/set/unset/replaceRoot/replaceWith/sortByCount。另支持 lookup/facet：lookup 只能访问当前数据库的普通集合；全树 64 stage、最多 4 层、每 facet 最多 8 分支，facet 内不可再嵌套 facet。递归拒绝 out/merge、JavaScript 与相应 BSON 类型。内置命令不设置 allowDiskUse/MaxTime；结果上限不限制服务器中间计算量。输入支持 Extended JSON v2，输出为 provider 转换后的 JSON，保留下来的嵌套 BSON 使用 Extended JSON；不承诺无损往返。

集合枚举、count、collection_info、explain 及原生索引管理不再开放。读取直接使用内置 aggregate，服务器追加行限制，provider 物化为 DataTable 后检查响应字节预算；该预算不限制读取期间内存。初始同步查询可能延迟取消，完成清理前不释放执行槽。配置阶段拒绝当前 provider 不支持的 SRV URI。

## 批量操作与事务

`insert_row` 使用 SqlSugar 的字典 `Insertable(...).AS(...).ExecuteReturnBigIdentityAsync(token)`，只插入一行并返回 `identityColumn` 和十进制字符串 `identity`（完整 Int64）。首版支持 SQL Server、MySQL、SQLite，要求 SqlSugar 能识别唯一整数自增列，SQLite 需要其可识别的 `AUTOINCREMENT` 定义；PostgreSQL 的当前字典自增返回路径不满足契约，暂不开放。`row` 为非空对象，最多 128 字段、4 MiB，字段区分大小写，禁止指定自增列，值仅 string/bool/number/null。省略列由数据库默认值处理；约束和触发器照常执行。需要显式 ADO 类型或二进制时使用 `execute_command`。

该工具要求 write 权限，CLI 需要 `--yes`，Web 需要工具名确认。超时包含排队与执行；同步元数据与 SQLite 取消遵循 SqlSugar/驱动限制，取消不代表回滚，不能自动重试。批量插入工具仍不提供。

当前内置元数据路径只接受普通表名；带 schema 的名称及 SQL Server 跨 schema 同名表在写入前拒绝，请改用 `execute_command` 显式指定目标。

```json
{"tableName":"orders","row":{"customer_name":"Alice","amount":10.5,"note":null},"databaseName":"primary","commandTimeoutSeconds":30}
```

`create_index`、`add_primary_key` 支持 `columnName` 或 `columnNames`（二选一），`drop_index` 删除指定索引。

`batch_sql_query` 仅接受 `commands: [{sql, parameters?}]`，最多五项；删除 queries，逐项状态及结果预算共享。DDL 仅使用 columns/column typed DTO，字段为 name/dataType/length/precision/scale/nullable/primaryKey/identity/comment/defaultExpression，未知字段拒绝；不再公开 SqlSugar DbColumnInfo。

`batch_execute_commands` 缺省 `atomic=false`，继续逐项执行，后续失败不回滚前面写入。显式 `atomic=true` 支持 SQLite、SQL Server、PostgreSQL，先验证全部普通 DML 和参数，再在一个连接的一次事务内执行。每项只接受一条 INSERT/UPDATE/DELETE；不接受 DDL、存储过程、多语句或事务控制。最多 100 项、4 MiB；totalTimeoutSeconds 为 1–300 且受服务器上限约束。事务截止包含准入和执行，每条命令还受单条 timeout 约束。

非原子批次耗尽总时限时，响应仍为 `success=false`、`errorCode=1011`、`retryable=false`。`data.totalCommands` 是批次大小，`data.results` 保留已知的逐项成功或失败。`data.unknownCommandIndex` 是已交给驱动、结果仍待核实的命令索引。`data.notExecutedFromIndex` 是尚未执行的尾段起点。两个索引都从 `0` 开始，无对应项时为 `null`。排队时超时会返回空 `results`，非空批次的未执行起点为 `0`。前面的写入可能已经生效。重试前先核对结果未知的命令，避免重复写入。

| transactionOutcome | 含义 |
|---|---|
| not-started | 事务未开始；预校验或准入失败 |
| committed | 已确认提交，committedAffectedRows 才计入写入数 |
| rolled-back | 已确认回滚，执行过的项标 rolled-back，后续项 not-executed |
| unknown | commit 应答不确定或 rollback 失败；核实数据库后再决定，禁止自动重放 |

取消会以独立 5 秒清理 token 尝试 rollback，并等待驱动清理后才释放 lease。CLI 能收到结果时输出 JSON 并退出 130。MCP 客户端取消后可能收不到最终结果，不能据此断言已回滚。事务保证不覆盖序列值、触发器外部副作用或分布式操作。

## Reader、分页与导出

`query_table_page` 可选 `columns`（1–64 个不重复字段）、`filters`（最多 32 项 AND 条件）和 `orderBy`（最多 8 项）。字段名区分大小写，必须为存在的普通字段，不接受 SQL 表达式。筛选支持 `eq/ne/gt/gte/lt/lte/isNull/isNotNull`；比较值为非空标量，可用 `dbType` 沿用现有参数的标量转换规则，空值判断省略 value/dbType。排序方向为 `asc/desc`，默认 asc，并追加未包含的全部主键作为排序末项。NULL 顺序遵循数据库默认规则。

传入任意新增选项（包括空 filters/orderBy）时返回 `pagingMode=offset`、`nextCursor=null`，使用 page 续页，且不能同时传 cursor。不执行 COUNT。data 只返回选列，orderBy 列出实际排序字段，effectiveOrderBy 报告方向。SqlSugar 的 Conditional/Select/Order 模型构建查询，直接调用 `ToOffsetDataTablePageAsync(page, pageSize)`，不带 total 参数；SQL Server 使用官方 OFFSET 分页，不暴露内部 RowIndex。官方 API 返回整页 DataTable 后，再检查最终 JSON 响应字节预算；该预算不限制读取期间内存，宽字段应缩小 pageSize 或选列。不传新增选项继续使用原 cursor 分支。

```json
{"tableName":"orders","columns":["id","amount"],"filters":[{"column":"status","op":"eq","value":"pending"}],"orderBy":[{"column":"created_at","direction":"desc"}],"page":1,"pageSize":50}
```

未指定新增选项的表分页和 MySQL/SQL Server 存储过程逐行读取时施加预算，无 DataTable 整页物化。表分页要求非空主键并使用复合主键的定义顺序，nextCursor 使用与对象分页相同的稳定配置绑定，并绑定表、键和 pageSize，拒绝与 page>1 混用。SQLite 普通表的可空 TEXT/复合主键及可空 INTEGER PRIMARY KEY DESC 在首请求返回 1004；INTEGER rowid 别名、显式非空主键及满足约束的 STRICT/WITHOUT ROWID 表可正常分页。保留显式 offset page 入口；cursor 不承诺跨页快照，主键更新仍可能改变集合。达到 pageSize 不额外读取 N+1，因此可能返回指向空末页的 cursor；字节超限返回 1013，不返回缺行的一页。存储过程必须完整消费所有结果才返回输出参数；达到行数/字节预算返回 1013，取消命令不回滚已产生的副作用。

MySQL 目录、外键/约束来自 information_schema，定义来自 SHOW CREATE；定义可能被驱动整体物化后再校验预算。缺失或不可见对象统一 definition-unavailable。其他引擎仍使用其原生元数据。

CLI 顶级 `export` 逐行输出 CSV/JSONL 到指定本地路径，支持 typed SQL 参数。默认 100000 行、256 MiB、30 秒，受服务端策略约束。临时文件位于目标目录，成功后发布；默认拒覆盖，显式 --overwrite 才替换。文件大小超限或失败不发布部分文件；行数触顶会发布已读取行并标记 limitReached（不能证明完整导出）。单行受服务端响应字节上限限制，CSV 保留公式前缀且 NULL 为空，JSONL 保留类型。stdout 只有摘要。

元数据、表分页、存储过程与导出共享包含准入、执行和读取的总截止时间。错误包含 `errorCode`、`retryable`、`phase`，并按场景提供 `capability` 和 `hint`。

执行指标见[数据库 MCP 工作流](database-mcp-workflow.md#元数据能力与观测)。SQLite 查询预算基准可按需运行 `pwsh -NoProfile -File 'scripts/benchmark.ps1' -Framework 'net10.0'`。

## MCP 协议扩展

MCP 还提供连接、能力和表结构 Resources，`analyze_slow_query` / `safe_schema_migration` Prompts、连接名前缀补全及健康检查进度通知。这些不计入 54 项工具，完整入口见[工具手册](../TOOLS.md)。

## 官方依据

- [SqlSugar 无实体查询](https://www.donet5.com/Home/Doc?typeId=2421)、[无实体插入](https://www.donet5.com/Home/Doc?typeId=2422)、[插入与自增返回](https://www.donet5.com/Home/Doc?typeId=1193)

- [MCP Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) 与 [stdio](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- [SQL Server SHOWPLAN_XML](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-showplan-xml-transact-sql)
- [PostgreSQL 17 EXPLAIN](https://www.postgresql.org/docs/17/sql-explain.html)
- [SQLite EXPLAIN QUERY PLAN](https://www.sqlite.org/eqp.html)
- [Mongo aggregation](https://www.mongodb.com/docs/manual/reference/operator/aggregation/) 与 [explain](https://www.mongodb.com/docs/manual/reference/command/explain/)

- [MySQL EXPLAIN](https://dev.mysql.com/doc/refman/8.4/en/explain.html) 与 [KEY_COLUMN_USAGE](https://dev.mysql.com/doc/refman/8.4/en/information-schema-key-column-usage-table.html)
- [Mongo lookup](https://www.mongodb.com/docs/manual/reference/operator/aggregation/lookup/) 与 [facet](https://www.mongodb.com/docs/manual/reference/operator/aggregation/facet/)（100 MB 阶段限制且不能 spill，最终 BSON 16 MiB）
