# MongoDB 集合集成契约（SqlSugar.MongoDbCore）

MongoDB 完全通过 `SqlSugar.MongoDbCore` 及其自带的 ADO 提供程序（`MongoDb.Ado.Data`）接入，使用同一套 `ISqlSugarClientFactory` / `SqlDatabaseSessions` 生命周期管理，不另设绕开 SqlSugar 的原生客户端缓存或直接调用 `IMongoDatabase`。

## 连接和执行

配置 `dbType: "MongoDb"`，URI 必须显式指定业务数据库，例如 `mongodb://user:password@localhost:27017/app?authSource=admin`。

当前 provider 不支持 `mongodb+srv://`，配置校验会明确拒绝；请使用集群提供的显式主机 `mongodb://` 连接字符串。

- 连接工厂与准入：`MongoCollectionService` 与 `DatabaseConnectionProbe` 统一从 `SqlDatabaseSessions` / `ISqlSugarClientFactory` 获取 `SqlSugarClient` 租约，使用 AOP 计时/日志/错误脱敏与总超时控制。
- 读取：直接调用 `Ado.GetDataReaderAsync("aggregate ...")`，使用 provider 内置命令。`mongo_find` 将筛选、排序、skip、limit 和投影转换为聚合 pipeline；没有自定义 handler、reader 或原生执行旁路。
- 探活：`MongoDbConnection.OpenAsync()` 不执行网络握手，因此 `test_connection` / `health_check` 通过内置 aggregate 读取至多一条文档的 `_id`，验证真实连接。取消限制与查询相同。
- 日志：Mongo AOP 记录命令事件和耗时，不记录 JSON 文档、过滤器或原始服务器异常文本。

## 工具（共 6 个）

| 工具 | 底层 SqlSugar 入口 | 必填与主要可选参数 | 权限与结果 |
| --- | --- | --- | --- |
| `mongo_find` | `Ado.GetDataReaderAsync("aggregate ...")` | `collection`；`filter?`、`projection?`、`sort?`、`skip=0`、`maxRows?` | Read；返回 `documents`，服务器限制行数，物化后检查响应字节预算 |
| `mongo_aggregate` | `Ado.GetDataReaderAsync("aggregate ...")` | `collection`、`pipeline`；`maxRows?` | Read；仅允许只读聚合 stage，禁止写入与 JavaScript |
| `mongo_insert_many` | `Ado.ExecuteCommandAsync("insertMany ...")` | `collection`、`documents` (1-1000) | Write；返回 `insertedCount`、`writeOutcome` |
| `mongo_update` | `Ado.ExecuteCommandAsync("updateMany ...")` | `collection`、`filter`、`update`；`allowAll=false` | Write + `enableDangerousOperations`；批量更新，返回 `modifiedCount` |
| `mongo_delete` | `Ado.ExecuteCommandAsync("deleteMany ...")` | `collection`、`filter`；`allowAll=false` | Write + `enableDangerousOperations`；批量删除，返回 `deletedCount` |
| `mongo_truncate` | `Ado.ExecuteCommandAsync("deleteMany ...")`，空 filter | `collection` | Schema + `enableDangerousOperations`；清空文档，保留集合和索引 |

## 读取和取消契约

- 输入仍接受 Extended JSON v2；输出使用 SqlSugar provider 转换后的值，不保证 BSON 无损往返。顶层 ObjectId 变为普通字符串，Int64 为 JSON 数字，Decimal128 转 CLR decimal，二进制变为 Base64 字符串且 subtype 丢失，日期为 UTC 字符串。超出 CLR decimal 范围的值导致查询失败，不静默截断。provider 保留的嵌套 BSON 值用 Canonical Extended JSON 序列化。
- provider 将结果中出现的顶层字段合并成列；某行缺失的字段与显式 null 都输出 null。ObjectId 输出字符串不能直接当作 ObjectId filter：需要显式传入 `{"_id":{"$oid":"返回的十六进制字符串"}}`，普通字符串不会自动推断类型。
- 两种读取都追加服务器 `$limit`；当前 provider 先物化该范围内的全部文档并转换 DataTable，再返回 reader。随后按 UTF-8 响应预算保留完整行。`limitReached=true, truncated=null` 表示达到行上限，不能证明还有更多数据；`truncated=true` 表示字节上限。
- 响应预算不限制读取期间内存或 Mongo 服务器中间计算量。宽字段应通过 projection 或较小 maxRows 控制；provider 物化阶段失败时，不返回尚未完成的部分结果。
- 设置 `Ado.CancellationToken`，并在执行前后检查取消。当前内置 aggregate 的初始请求是同步执行，不能由请求 token 立即中断；返回时间受 provider/驱动超时约束。执行槽和租约一直保留到调用及清理结束，不用后台任务提前返回。当前内置命令没有传入 `MaxTime` 或 `allowDiskUse`，遵循服务器默认值。写入取消或超时不保证回滚，`writeOutcome=unknown` 时先核实数据。
- ADO 命令以空白分隔集合名，因此集合名不接受空白字符；继续禁止 NUL、`$` 和 `system.` 前缀。

## 迁移

`mongo_insert_one` 改为 `mongo_insert_many`，单条写入也传入 `documents: [document]`；不再返回 `insertedId`。`mongo_update`、`mongo_delete` 按多文档执行，均要求 `enableDangerousOperations=true`，空 filter 还要求 `allowAll=true`；移除旧的 `many`、`upsert` 参数及 `matchedCount`、`upsertedId` 返回字段。

## 能力边界

- `SqlSugar.MongoDbCore 5.1.4.280` / `MongoDb.Ado.Data` 仅原生支持 `find`、`aggregate`、`insertMany`、`updateMany`、`deleteMany`、`bulkWrite` 六类命令与 `TruncateTable`；不暴露 `mongo_list_collections`、`mongo_count`、`mongo_distinct`、`mongo_collection_info`、`mongo_collection_stats`、`mongo_explain`、`mongo_create_index`、`mongo_drop_index` 及 `mongo_insert_one`。
- 通用 SQL / Schema 工具（`query_database`、`get_table_schema`、`list_objects`、`explain_query`、`create_table` 等）对 MongoDB 继续返回 1012（`UnsupportedCapability`）。
