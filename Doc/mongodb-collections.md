# MongoDB 集合查询与写入契约

本版本提供六个集合工具，总工具数为 62。关系型数据库继续使用 SQL 工具；MongoDB 使用官方 `MongoDB.Driver 3.11.1`，不接受 SQL、mongosh 脚本或任意 `runCommand`。

## 连接和执行

配置 `dbType: "MongoDb"`，URI 必须显式指定业务数据库，例如 `mongodb://user:password@localhost:27017/app?authSource=admin`。`authSource` 只选择认证库，不能替代 `/app`。`mongodb+srv://` 由官方驱动解析。`maxPoolSize`、TLS、retryWrites 等选项放在 URI，`optimizationSettings` 不覆盖它们。

调用沿用现有准入租约，排队前固定连接、配置版本和权限；不改变全局当前连接。原生 MongoClient 按连接身份和精确 URI 复用，配置大小写有意义。旧配置在途请求保持原目标，最后一个租约释放时回收旧客户端；注册表最多 128 项，满额只淘汰空闲项，全部在途时返回 1009。移除但未再次调用的配置占用空闲项，随后容量淘汰或宿主关闭时回收。

SqlSugar 官方提供原生对象入口，但当前 `SqlSugar.MongoDbCore 5.1.4.280` 的 bridge 有大小写不敏感静态缓存和 SRV 解析限制。因此集合路径不访问 `Ado.Connection`，原生客户端由 `MongoClientRegistry` 独立管理。`test_connection`、`test_connection_by_name`、health check、重试检测及 CLI/Web 在线诊断统一执行原生 ping。capabilities、配置校验和 doctor 离线模式不解析 DNS、不触网。

## 工具

所有工具支持 `databaseName`、`commandTimeoutSeconds`、`maxResponseBytes`，对应 CLI `--database-name`、`--command-timeout-seconds`、`--max-response-bytes`。

| 工具 | 必填与主要可选参数 | 权限与结果 |
| --- | --- | --- |
| `mongo_list_collections` | `namePrefix?`、`maxRows?` | Read；`collections` 中的 name/type；prefix 按字面匹配 |
| `mongo_find` | `collection`；`filter?`、`projection?`、`sort?`、`skip=0`、`maxRows?` | Read；`documents`，不隐式 count |
| `mongo_count` | `collection`；`filter?` | Read；精确 `matchedCount` |
| `mongo_insert_one` | `collection`、`document` | Write；`insertedId`、`acknowledged`、`writeOutcome` |
| `mongo_update` | `collection`、`filter`、`update`；`many=false`、`upsert=false`、`allowAll=false` | Write；`matchedCount`、`modifiedCount`、可能有 `upsertedId` |
| `mongo_delete` | `collection`、`filter`；`many=false`、`allowAll=false` | Write；`deletedCount` |

MCP 参数必须是真实 JSON 对象，CLI 用 JSON 文本绑定成同一对象，Web 使用 JSON 输入框。拒绝数组、标量、显式 null、重复字段、超过 64 层或总量超过 4 MiB 的对象输入。BSON 文档上限 16 MiB。集合名禁止空白、NUL、`$` 和 `system.` 前缀；namespace 上限 255 UTF-8 字节（分片环境可能有更严格限制）。

```json
{"name":"mongo_find","arguments":{"databaseName":"mongo-main","collection":"orders","filter":{"customerId":{"$oid":"507f1f77bcf86cd799439011"}},"projection":{"customerId":1,"total":1},"sort":{"_id":1},"maxRows":20}}
```

```powershell
DatabaseMcpServer tool mongo_find --collection 'orders' --filter '{"customerId":{"$oid":"507f1f77bcf86cd799439011"}}' --max-rows 20 --database-name 'mongo-main'
DatabaseMcpServer tool mongo_insert_one --collection 'orders' --document '{"_id":"order-42","total":{"$numberDecimal":"12.50"}}' --database-name 'mongo-main' --yes
DatabaseMcpServer tool mongo_update --collection 'orders' --filter '{"_id":"order-42"}' --update '{"$set":{"status":"paid"}}' --database-name 'mongo-main' --yes
```

## BSON、查询和分页

输入接受严格 JSON / Extended JSON v2，文档和 ID 固定输出 Canonical Extended JSON v2。ObjectId 用 `$oid`，64 位整数用 `$numberLong`，Decimal128 用 `$numberDecimal`，UTC 日期用 `$date`，binary/UUID 用 `$binary`。普通字符串不会自动变成 ObjectId。envelope 的计数、状态和配置版本仍是普通 JSON 值。

filter 递归禁止 `$where`、`$function`、`$accumulator`；所有输入禁止 JavaScript BSON 类型。其他查询操作符交给 Mongo 校验。projection 只支持 0/1，包含和排除不能混用，`_id` 除外。sort 只支持 1/-1，默认 `_id:1`，用户排序缺少 `_id` 时追加它。skip 限制为 0–100000；深偏移开销大，优先索引字段范围过滤。并发写入会移动分页结果，不提供跨请求快照。

查询默认 1000 行、1 MiB、30 秒，受服务端 `DB_QUERY_*` 上限约束；所有响应至少要求 1024 字节预算。游标逐批读取，batchSize ≤ 64，find 服务端 limit 等于行数预算；不全量物化，不读取第 N+1 条探测尾部。字节预算包含整个工具 JSON，不计 MCP text/structuredContent 的重复传输开销，也不是驱动内存或数据库计算量限制。

- `limitReached=true,truncated=null`：触及行数上限，是否还有下一条未知，即使结果恰好等于上限也如此。
- `limitReached=true,truncated=true`：遇到放不下的完整文档；首条超大时返回空数组并明确这两个标志，不能解释为没有匹配结果。
- `limitReached=false,truncated=false`：游标读完。正常、超限、异常、超时和取消路径均释放游标。

Driver 3.11.1 XML 中的操作级 `Timeout` 实际为 internal，未使用反射调用。实现用共享 CancellationToken 截止时间覆盖准入、原生初始化、命令和全部 getMore，find/count 同时使用公开 MaxTime。0 仅在服务端允许时代表无应用截止时间，URI 中更短的驱动超时仍可能生效。SRV 使用显式 `ResolveAsync(bool, token)`，避免该版本 token-only 重载丢弃 token 的问题。TXT 合并后通过官方 builder 离线生成 settings，再保留 SRV scheme、seed、serviceName/maxHosts 与 TLS 设置，避免 `FromUrl` 再次同步查 TXT；持续 SRV 发现仍由驱动负责。

## 写保护和结果

服务端 `DB_TOOL_ACCESS` 与连接 `accessMode` 取更严格权限。写工具要求 Write，CLI 必须 `--yes`，Web 必须输入工具全名；MCP 标注 Destructive，权限仍在服务端校验。

update 只接受非空操作符对象：`$set/$unset/$inc/$mul/$min/$max/$push/$pull/$addToSet/$pop/$currentDate/$setOnInsert`；禁止修改 `_id` 或其子路径，不开放 replacement/pipeline。操作符下嵌套的业务数据不会当成顶层命令。缺少 filter 与显式 `{}` 不同。

many=true 总是要求连接 `enableDangerousOperations=true`。空 filter 还要求 `allowAll=true`，即使是 one 也如此。非空 filter 仍可能覆盖全部集合，这不是范围证明。many 与 upsert 不能同时启用。默认 UpdateOne/DeleteOne；多个匹配中选哪条由数据库决定，多文档修改不具备事务原子性。

拒绝 `w=0`，不改写用户 write concern；驱动 retryWrites 沿用连接配置，应用层不重试。insert 缺少 `_id` 时客户端先生成 ObjectId，并在发送前验证 ID 确认结果能放入预算。

| writeOutcome | 含义 |
| --- | --- |
| `acknowledged` | 服务器已确认；matched=0、modified=0 也可能正常成功 |
| `not-executed` | 校验、权限、初始化等阶段失败，尚未调用写入 |
| `unknown` | 已调用写入但未取得成功确认，可能提交或部分生效；需核实后再决定重试 |

请求取消继续传播为协议取消，不保证返回 writeOutcome；取消不回滚提交的写入。若 upsert 返回的 ID 使响应超预算，返回 1013 并保留 `writeOutcome=acknowledged`，不能解释为没有执行。

错误不包含原始 Mongo 异常、连接 URI、键值或文档内容。1001 连接/认证，1002 数据库执行，1004 参数，1009 准入/客户端容量，1010 权限，1011 操作超时，1012 provider 不适用，1013 响应超限。标准错误可能不遵循非法输入的字节预算。

## 验证及边界

MongoDB 8.0 standalone Docker 验证 CRUD、one/many、upsert、行/字节限制、多批游标、认证/重复键脱敏、故障注入、超时/取消、原生 ping，以及真实 stdio、CLI 和 Web 绑定。独立 opt-in：`DBMCP_RUN_MONGODB_INTEGRATION=1`、`DBMCP_TEST_MONGODB`；推荐脚本自动提供临时凭据、loopback 随机端口、标签校验和 finally 清理。脚本打印镜像 digest 并检查 TRX 零失败/零跳过。

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'Category!=Integration&Category!=MongoIntegration'
pwsh -NoProfile -File 'scripts/test-mongodb-integration.ps1'
pwsh -NoProfile -File 'scripts/test-package.ps1'
```

未开放 aggregation、任意 runCommand、集合删除、索引管理、批量插入、事务和 change stream。SRV 路由和离线校验有测试；真实 SRV DNS/TLS、副本集、分片和跨平台运行不在本轮实库验收范围。SqlSugar SQL/DDL 工具对 Mongo 继续返回 1012。

依据：[SqlSugar Mongo 原生对象](https://www.donet5.com/Doc/1/2651)、[官方 MongoClient](https://www.mongodb.com/docs/drivers/csharp/current/connect/mongoclient/)、[查询](https://www.mongodb.com/docs/drivers/csharp/current/crud/query/find/)、[更新](https://www.mongodb.com/docs/drivers/csharp/current/crud/update-one/)、[Extended JSON v2](https://www.mongodb.com/docs/manual/reference/mongodb-extended-json/)。

## 2026-11-20 验收记录

Windows x64，源码与测试双框架，测试过程未使用子代理：

| 检查 | net9.0 | net10.0 |
| --- | --- | --- |
| 非远程集成回归 | Passed: 435, Failed: 0, Skipped: 0 | Passed: 435, Failed: 0, Skipped: 0 |
| MongoDB 8.0 Docker | Passed: 10, Failed: 0, Skipped: 0 | Passed: 10, Failed: 0, Skipped: 0 |
| MySQL / PostgreSQL / SQL Server Docker | Passed: 38, Failed: 0, Skipped: 0 | Passed: 38, Failed: 0, Skipped: 0 |
| 最终 MCP 对象 schema/错误契约复验 | Passed: 2, Failed: 0, Skipped: 0 | Passed: 2, Failed: 0, Skipped: 0 |

Mongo 镜像：`mongo:8.0`，digest `sha256:bdc216e9d5a6ec2cfdac789b5832fdab64cd32055f59885cae918a92de63c361`。本轮测试容器及匿名卷均已清理，保留原有容器。

定向测试类：`MongoDocumentContractTests`、`MongoClientRegistryTests`、`MongoCollectionServiceTests`、`MongoIntegrationTests`、`McpProtocolTests`。额外暂时反转批量写保护条件，`ManyAndEmptyFilter_RequireExplicitDangerousOptIn` 如预期失败；恢复原文件后回归通过。

CLI catalog 覆盖 62/62，43 个成功退出，19 个非零退出（包含 SQLite 不支持的 schema/procedure 工具及六个 Mongo 工具）。六个 Mongo 案例逐项断言错误 1012，真实成功路径由 Mongo 集成覆盖。包测试包含 net10 临时安装、62 tools、EJSON 对象 schema、标准错误、完整 native assets 和缺 HG 扩展隔离检查；约 149.2 MiB，低于 250,000,000 字节。生产/测试项目及传递依赖的 NuGet.org 审计未报告已知漏洞。
