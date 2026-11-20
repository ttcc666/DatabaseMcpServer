# PowerShell 命令示例

以下示例使用当前 CLI 契约。先替换配置路径、连接名和业务对象；写操作需要用户授权。SQL 方言由目标数据库决定。

## 帮助和命名目标

```powershell
DatabaseMcpServer tool list
DatabaseMcpServer tool help query_database
DatabaseMcpServer tool help batch_execute_commands
DatabaseMcpServer config help update
DatabaseMcpServer tool get_database_capabilities --database-name 'reporting' --config 'D:/config/databases.json'
DatabaseMcpServer tool test_connection_by_name --database-name 'reporting' --config 'D:/config/databases.json'
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

工具按主键排序；并发写入仍可能让数据在页间移动。需要筛选、关联或自定义 SQL 时使用 query_database。

## 批量查询与写入

```powershell
DatabaseMcpServer tool batch_sql_query `
  --queries '["select count(*) from users","select count(*) from roles"]' `
  --max-rows 100 --max-response-bytes 1048576 `
  --command-timeout-seconds 60 `
  --database-name 'reporting' --config 'D:/config/databases.json'

DatabaseMcpServer tool batch_execute_commands `
  --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"delete from sessions where user_id=@id","parameters":[{"name":"id","value":1}]}]' `
  --command-timeout-seconds 60 `
  --database-name 'primary' --config 'D:/config/databases.json' --yes
```

- batch_sql_query 最多 5 条，只读，无逐条参数；需要绑定参数时分次调用 query_database。
- batch_execute_commands 使用命令对象，每项内嵌自己的参数，不使用平行参数数组。
- SQL 超时作用于每条命令；批量查询行数和字节预算在整批共享，预算耗尽的剩余项标记 executed=false。
- 检查每个 results 项。批量写没有自动回滚，前面的成功写入可能已经提交。

## 超时与预算

query_database 默认 1000 行、1 MiB、30 秒；query_table_page 默认 100 行。其他 SQL 工具未启用默认预算时使用 provider 超时（通常 300 秒），仍受服务端限制。

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from large_report where day = @day' `
  --parameters '[{"name":"day","value":"2026-11-20","dbType":"Date"}]' `
  --command-timeout-seconds 900 --max-rows 500 `
  --database-name 'reporting' --config 'D:/config/databases.json'
```

0–86400 是语法范围，实际最大值由 DB_COMMAND_MAX_TIMEOUT_SECONDS 限制；0 还要求 DB_ALLOW_INFINITE_TIMEOUT=true。Mongo URI 中的驱动超时仍可能更短。响应字节指工具 JSON，不能等同协议传输量或进程内存上限。

## Mongo 集合

URI 必须包含业务数据库；authSource 不是查询目标。使用 JSON / Extended JSON v2 对象，PowerShell 单引号能保留 $oid、$set 等字段。

```powershell
DatabaseMcpServer tool mongo_list_collections --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_find `
  --collection 'orders' --filter '{"customerId":{"$oid":"507f1f77bcf86cd799439011"}}' `
  --projection '{"customerId":1,"total":1}' --sort '{"_id":1}' --max-rows 20 `
  --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_count --collection 'orders' --filter '{"status":"paid"}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json'

DatabaseMcpServer tool mongo_insert_one --collection 'orders' `
  --document '{"_id":"order-42","total":{"$numberDecimal":"12.50"}}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes

DatabaseMcpServer tool mongo_update --collection 'orders' --filter '{"_id":"order-42"}' `
  --update '{"$set":{"status":"paid"}}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes

DatabaseMcpServer tool mongo_delete --collection 'orders' --filter '{"_id":"order-42"}' `
  --database-name 'mongo-main' --config 'D:/config/databases.json' --yes
```

Mongo 输出文档为 Canonical Extended JSON v2。普通字符串不会自动转换成 ObjectId；Int64 用 $numberLong，Decimal128 用 $numberDecimal。

many=true 要求 enableDangerousOperations=true；空 filter 另外要求 allowAll=true，即使默认 one 操作也一样。默认 UpdateOne/DeleteOne；many 与 upsert 不可同时启用。update 仅接受支持的操作符对象，不接受 replacement/pipeline。没有 aggregation、任意 runCommand、批量插入或事务工具。

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
