# PowerShell 命令示例

以下示例使用当前 CLI 契约。先替换配置路径、连接名和业务对象；写操作需要用户授权。SQL 方言由目标数据库决定。

## 帮助和命名目标

```powershell
DatabaseMcpServer --version
DatabaseMcpServer tool list --json
DatabaseMcpServer tool help query_database
DatabaseMcpServer tool help batch_execute_commands
DatabaseMcpServer config help update
DatabaseMcpServer tool get_database_capabilities --database-name 'reporting' --config 'D:/config/databases.json'
DatabaseMcpServer tool test_connection --database-name 'reporting' --config 'D:/config/databases.json'
DatabaseMcpServer tool get_table_schema --table-name 'users' --database-name 'reporting' --refresh true --config 'D:/config/databases.json'
```

帮助输出到 stderr。支持的工具用 `--database-name` 固定本次目标，无需先切换当前库。

## 类型化 SQL 参数

PowerShell 用单引号包住整个 JSON；内部 SQL 单引号写成两个单引号。每个对象或数组作为一个 argv 参数传入。

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from users where age >= @age and city = @city' `
  --parameters '[{"name":"age","value":18,"dbType":"Int32"},{"name":"city","value":"北京","dbType":"String"}]' `
  --max-rows 100 --max-response-bytes 1048576 `
  --database-name 'reporting' --config 'D:/config/databases.json'

DatabaseMcpServer tool query_database `
  --sql 'select * from users where id in (@ids)' `
  --parameters '[{"name":"ids","value":[1,2,3]}]' `
  --database-name 'reporting' --config 'D:/config/databases.json'
```

NULL 必须带 dbType，例如 `[{"name":"note","value":null,"dbType":"String"}]`。日期用明确的 ISO 8601 字符串，二进制用 base64；不要先把大整数转成 JavaScript Number，也不要通过拼接 SQL 插入值。IN 数组使用 JSON 值数组，当前不能声明 dbType，否则返回 1004；不要把标量类型声明套到数组上。

SQL Server 存储过程参数示例（输出 String 需要 size）：

```powershell
DatabaseMcpServer tool call_stored_procedure `
  --procedure-name 'dbo.report_summary' `
  --parameters '[{"name":"year","value":2026,"dbType":"Int32"},{"name":"summary","value":null,"dbType":"String","direction":"Output","size":200}]' `
  --database-name 'sqlserver-main' --config 'D:/config/databases.json' --yes
```

Output / InputOutput 支持 SQL Server、MySQL；ReturnValue 支持 SQL Server。输出 Decimal precision 不支持，不要声称驱动会保留未传递的精度设置。

## 按表分页

表有主键且只需按表读取时，使用 query_table_page。以下读取第二页，每页 20 行，不隐式查询总行数：

```powershell
DatabaseMcpServer tool query_table_page --table-name 'users' --page 2 --page-size 20 `
  --database-name 'reporting' --config 'D:/config/databases.json'
```

不传新增选项时按主键排序，可使用 nextCursor 续页。选列、AND 筛选及排序可直接传给 query_table_page：

```powershell
DatabaseMcpServer tool query_table_page --table-name 'users' --page 1 --page-size 20 `
  --columns '["id","name"]' --filters '[{"column":"status","op":"eq","value":"active"}]' `
  --order-by '[{"column":"id","direction":"desc"}]' `
  --database-name 'reporting' --config 'D:/config/databases.json'
```

任一选项存在即进入 offset 分页，不返回 cursor，也不能传 cursor；实际排序追加缺失主键。字段名区分大小写，筛选支持 eq/ne/gt/gte/lt/lte/isNull/isNotNull；空值判断省略 value/dbType。整页物化后检查响应预算，不限制读取期间内存。两种路径都要求非空主键；并发写入仍可能让数据在页间移动，关联和复杂表达式使用 query_database。

## 单行插入与复合索引

```powershell
DatabaseMcpServer tool insert_row --table-name 'users' --row '{"name":"Alice","email":null}' `
  --database-name 'primary' --config 'D:/config/databases.json' --yes
DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_status_id' `
  --column-names '["status","id"]' --database-name 'primary' --config 'D:/config/databases.json' --yes
DatabaseMcpServer tool drop_index --table-name 'users' --index-name 'IX_users_status_id' `
  --database-name 'primary' --config 'D:/config/databases.json' --yes
```

insert_row 支持 SQL Server/MySQL/SQLite 的普通表名和唯一整数自增列（SQLite 需要 AUTOINCREMENT），row 不得包含自增列或嵌套值。结果为字符串 identity 和 identityColumn，省略列使用数据库默认值。PostgreSQL、显式类型或 schema 目标使用 execute_command。create_index/add_primary_key 的 columnName 与 columnNames 二选一；权限和 provider 限制仍生效。

## 批量查询与写入

```powershell
DatabaseMcpServer tool batch_sql_query `
  --commands '[{"sql":"select count(*) as total from users"},{"sql":"select count(*) as total from roles"}]' `
  --max-rows 100 --max-response-bytes 1048576 `
  --command-timeout-seconds 60 `
  --database-name 'reporting' --config 'D:/config/databases.json'

DatabaseMcpServer tool batch_execute_commands `
  --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"delete from sessions where user_id=@id","parameters":[{"name":"id","value":1}]}]' `
  --command-timeout-seconds 60 `
  --database-name 'primary' --config 'D:/config/databases.json' --yes
```

- batch_sql_query 最多 5 项，只读；每项 commands 对象支持 sql 与 parameters，不接受旧 queries。
- batch_execute_commands 使用命令对象，每项内嵌自己的参数，不使用平行参数数组。
- 非原子 SQL 批次的超时覆盖排队及整批执行；原子批次仍由 totalTimeoutSeconds 控制总时限、commandTimeoutSeconds 控制单条命令。批量查询行数和字节预算在整批共享，预算耗尽的剩余项标记 executed=false。
- 检查每个 results 项。默认批量写没有自动回滚；--atomic true 使用单次事务，检查 transactionOutcome，unknown 必须核实实际状态。

## 超时与预算

query_database 默认 1000 行、1 MiB；query_table_page 默认 100 行。异步 SQL 的总超时默认服务端查询超时（通常 30 秒），覆盖排队、执行和读取。同步 GO/DDL 保留驱动命令超时。

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from large_report where day = @day' `
  --parameters '[{"name":"day","value":"2026-11-20","dbType":"Date"}]' `
  --command-timeout-seconds 900 --max-rows 500 `
  --database-name 'reporting' --config 'D:/config/databases.json'
```

0–86400 是语法范围，实际最大值由 DB_COMMAND_MAX_TIMEOUT_SECONDS 限制；0 还要求 DB_ALLOW_INFINITE_TIMEOUT=true。Mongo URI 中的驱动超时仍可能更短。响应字节指工具 JSON，不能等同协议传输量或进程内存上限。

## Mongo 集合

URI 必须包含业务数据库，当前不支持 mongodb+srv；authSource 不是查询目标。使用 JSON / Extended JSON v2 对象，PowerShell 单引号能保留 $oid、$set 等字段。集合名从用户或已有上下文取得，当前不提供集合枚举工具。

```powershell
DatabaseMcpServer tool get_database_capabilities --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_find `
  --collection 'orders' --filter '{"customerId":{"$oid":"507f1f77bcf86cd799439011"}}' `
  --projection '{"customerId":1,"total":1}' --sort '{"_id":1}' --max-rows 20 `
  --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_aggregate --collection 'orders' --pipeline '[{"$match":{"status":"paid"}},{"$count":"total"}]' `
  --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_insert_many --collection 'orders' `
  --documents '[{"_id":"order-42","total":{"$numberDecimal":"12.50"}}]' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes

DatabaseMcpServer tool mongo_update --collection 'orders' --filter '{"_id":"order-42"}' `
  --update '{"$set":{"status":"paid"}}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes

DatabaseMcpServer tool mongo_delete --collection 'orders' --filter '{"_id":"order-42"}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes
```

Mongo 输入中普通字符串不会自动转换成 ObjectId；显式类型使用 $oid、$numberLong、$numberDecimal。输出为 provider 转换后的 JSON：顶层 ObjectId 为字符串、Int64 为数字、缺失字段与 null 合并，不保证 BSON 无损往返。读取 ObjectId 后用于筛选需重新包装 $oid。$count 在没有输入行时可能返回空 documents，不要直接读取不存在的第一项。

以上 update/delete 示例要求连接 enableDangerousOperations=true；两者始终批量执行，空 filter 还需 allowAll=true。many/upsert 已移除。insert_many 接受 1–1000 个对象，返回 insertedCount，不返回 insertedId。update 仅接受支持的操作符对象，不接受 replacement/pipeline。mongo_truncate 清空文档并保留集合/索引，要求 schema-admin 和危险操作开关；read-write 下已获授权的清空可用 mongo_delete 的空 filter 加 allowAll。没有任意 runCommand、集合元数据/explain、索引或事务工具。

读取先由 provider 物化，再检查响应字节；初始同步 aggregate 可能延迟取消，返回时间受驱动行为约束。较宽文档应缩小 maxRows 或使用 projection。

写后检查 writeOutcome：acknowledged / not-executed / unknown。unknown 或取消都不能推断没有提交，先按 _id 核实实际状态，避免重复写入。

## SQL Server 字面值与 GO

```powershell
DatabaseMcpServer tool add_default_value --table-name 'users' --column-name 'status' `
  --default-value '''active''' --database-name 'sqlserver-main' `
  --config 'D:/config/databases.json' --yes

$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@

DatabaseMcpServer tool execute_command_with_go --sql $sql `
  --database-name 'sqlserver-main' --config 'D:/config/databases.json' --yes
```

default-value 接受 SQL 字面值。GO 脚本必须是包含实际换行的单个参数，GO 单独一行。自动化优先 ProcessStartInfo.ArgumentList；它避免手工 shell 转义，但参数内容仍须符合工具 JSON/schema。

## Cursor、批量结构和导出

```powershell
DatabaseMcpServer tool query_table_page --table-name 'users' --page-size 20 --cursor '<上次 nextCursor>'
DatabaseMcpServer tool get_table_schema --table-names '["users","roles"]' --sections '["columns","foreignKeys"]'
DatabaseMcpServer export --sql 'SELECT id,name FROM users' --format 'jsonl' --output 'D:\exports\users.jsonl' --max-rows 100000 --max-file-mib 256 --command-timeout-seconds 30
```

cursor 绑定连接、配置内容和路径、工作目录、表及页大小。配置或查询条件变化后需从第一页开始。表分页要求非空主键，主键并发更新可能影响后续页。多表结构共享预算，逐项检查 `status`。导出通过 `--overwrite` 显式允许覆盖，stdout 返回摘要。CSV 保留公式字符，NULL 写为空值，JSONL 保留类型。DDL 使用 `columns` 或 `column`，并用 `name`、`dataType` 等字段描述列。
