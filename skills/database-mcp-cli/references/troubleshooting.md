# CLI 故障排查

先确认可执行文件来源、config 路径和目标数据库，使用实际 tool help；完整示例见 [commands.md](commands.md)。

## 退出码与输出

| 现象 | 定位与处理 |
| --- | --- |
| exit 2，提示需要 --yes | CLI 确认未通过，尚未执行工具；确认用户授权后补 --yes |
| exit 2，未知参数、JSON 类型错误 | 按帮助修正参数名及类型，不先修改数据库权限 |
| exit 2，找不到配置或启动异常 | 查看 stderr 和显式 --config，不能一概归为 SQL 错误 |
| exit 1，success=false | 检查 errorCode、errorMessage 和部分保存/写入状态 |
| exit 0，批量包含失败项 | 逐条检查 results，顶层成功不保证子项成功 |
| exit 130 | 已取消；写入结果需要核实，不能直接重放 |
| stdout 空，stderr 有帮助 | 帮助与目录的正常输出方式 |
| 查询 JSON 混入日志 | 分开捕获 stdout/stderr，记录最小复现，排查驱动输出 |
| get_database_config 没有 success | 使用退出码及 configured 等实际字段判断 |

## 旧工具或参数不能绑定

普通 SQL 查询用 query_database，IN 查询通过参数数组绑定；按表分页用 query_table_page。当前契约不提供旧 sql_query、sql_query_with_in、get_table_data 和导出工具的兼容入口。先用实际 tool list/help 确认安装版本的能力。

- SQL parameters 必须是对象数组，不是 name:value 字典。
- batch_execute_commands 的 commands 必须是命令对象数组，每项嵌入 parameters；没有 --parameters-array。
- 存储过程通过 parameters 的 direction 表达输入输出，没有 --input-parameters / --output-parameters。
- SQL NULL 必须带 dbType；输出字符串/二进制必须带正数 size。未知字段和不能精确转换的数值应修正，不通过字符串插值绕过参数绑定。
- IN 数组不能声明 dbType；使用 `[{"name":"ids","value":[1,2,3]}]` 绑定 `IN (@ids)`。
- Mongo filter/document/update 是对象，不是字符串值、数组或显式 null。CLI 把整个 JSON 文本作为一个 argv 传入。

```powershell
DatabaseMcpServer tool help query_database
DatabaseMcpServer tool help batch_execute_commands
DatabaseMcpServer tool help call_stored_procedure
DatabaseMcpServer tool help mongo_update
```

## 文档有工具，当前命令却找不到

```powershell
Get-Command 'DatabaseMcpServer' -All | Select-Object Source
dotnet tool list --global
DatabaseMcpServer tool list
DatabaseMcpServer tool help query_database
```

先确认 PATH 实际命中的可执行文件和安装版本。单独工具目录安装用 `dotnet tool list --tool-path '<安装目录>'` 查询该目录，并通过其中的可执行文件检查帮助。确认版本差异后按用户要求升级工具；不要把另一版本的工具名称或参数直接套用到当前命令。程序 --version 无效。

## 连接、配置和默认库

- tool/-web 默认会先看当前目录，再看 DB_CONFIG_PATH；确定性脚本传 --config。
- init/config 默认用户目录；不能假定它们跟随 tool 的查找顺序。
- switch_database 改 CLI 状态，config use 改文件默认。单次操作用 --database-name 可以避免影响其他调用。
- 当前名称仍存在时 reload_database_config 保留选择；需要切换时执行 switch_database，不删除整个 cli-state.json 影响其他配置。
- 配置操作 success=false 时查看 saved/runtimeApplied/selectionPersisted 和 databaseName。先读取实际文件和运行状态，避免重复已成功的保存。
- config doctor 默认在线。离线诊断需 --test-connections false；get_database_capabilities 不做连通性测试。
- Mongo URI 必须包含业务库路径；authSource 仅是认证库，不能替代目标库。

## 权限和 provider

| errorCode | 检查重点 |
| --- | --- |
| 1001 | 连接、认证、网络；避免把原始连接串贴进报告 |
| 1002 | SQL/数据库执行错误；检查目标、schema 和参数 |
| 1003 | 危险操作保护及连接 enableDangerousOperations |
| 1004 | 输入类型、范围、预算或不支持的参数形状 |
| 1009 | 准入队满/等待超时，或 Mongo 原生客户端容量 |
| 1010 | DB_TOOL_ACCESS 与 accessMode；--yes 不覆盖权限 |
| 1011 | 超时；确认服务端及驱动限制 |
| 1012 | provider 不支持该操作或所需驱动不可用 |
| 1013 | 响应超预算；写操作可能已经执行 |

先看 get_database_capabilities 的实际 provider、驱动可用性和操作能力。DB2/Hana 当前不可用，不是补一个任意驱动包就能打开的工具能力。其他 provider 随包带驱动也可能有原生 SDK / 平台 / 产品验证限制。不要把 SQLite 不支持存储过程误诊为需要重新安装工具。

## 超时与截断

query_database、Mongo 默认 30 秒；其他 SQL 工具未开启默认预算时通常使用驱动的 300 秒。所有请求都受服务端限制；0 秒要求允许无限等待，Mongo URI 超时仍可能更短。

SQL 批量的超时作用于每条命令，行数/字节预算在批量查询中共享。Mongo 的操作截止时间覆盖排队和读取流程。truncated=null 表示未探测尾部，不能当作 false；空数据加截断标志也不能解释为没有匹配记录。

## 批量写与 Mongo 写结果

批量写不是事务。根据每项结果及数据库实际状态决定后续操作，不重放已成功的项目。

Mongo writeOutcome：

- acknowledged：已确认；matchedCount/modifiedCount=0 也可能是正常成功。
- not-executed：尚未调用写入，修正错误后才考虑执行。
- unknown：写入已被调用但没有成功确认，可能提交或部分生效；先按 _id 查询确认。

取消可能不返回 writeOutcome；同样不能推断回滚。若 upsert 已确认后结果 ID 超出响应预算，可能同时出现 errorCode=1013 和 writeOutcome=acknowledged。保留这种信息，不自动再次写入。

many=true 要求 enableDangerousOperations=true；空 filter 还要求 allowAll=true，包括 one 操作。many 与 upsert 互斥。Mongo 不支持任意 runCommand、JavaScript filter 或替换/管道更新。

## PowerShell 与本地 Web

- default-value 接受 SQL 字面值：字符串 active 在 PowerShell 中写作 `--default-value '''active'''`。
- GO 使用 here-string 和真实换行；JSON 用单引号，避免 $set/$oid 被变量展开。
- -web 端口只接受 0–65535；页面仅供本机。
- Web 写入 403 先检查 X-DatabaseMcp-Web 和同源 Origin；不要关闭这些保护。
- Web 的数字和布尔参数用 JSON 数字/布尔值，不能传 "2"/"true"。

## SQL Server TLS

保留实际错误证据，先检查客户端 TLS 支持、驱动及服务器证书链。不要静默修改现有 TLS 策略。只有用户已授权诊断性降低校验时才使用临时配置，并说明 TrustServerCertificate 跳过身份验证、Encrypt=False 降低传输保护；连接成功不代表安全配置等价。

## 验证范围

验证 CLI 调用时，区分实际执行成功、能力不支持和执行失败。SQLite 不支持某项操作不能证明其他数据库也不支持；Mongo CRUD 成功需要对应实例的实际结果。仅能列出工具不代表连接或读写成功，报告应说明实际验证过的连接和操作。
