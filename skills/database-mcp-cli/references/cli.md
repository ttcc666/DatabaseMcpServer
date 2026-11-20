# CLI 配置与执行工作流

通过已安装的命令行工具管理连接和执行数据库操作。完整工具参数以目标可执行文件的 tool list/help 为准，具体 SQL/Mongo 示例见 [commands.md](commands.md)。

## 运行环境、版本和帮助

当前工具包需要 .NET 10 运行时及 ASP.NET Core 共享框架；提示缺少框架时，用 dotnet --list-runtimes 核对。安装与升级按用户请求执行，已有可用工具时直接查询帮助：

```powershell
dotnet tool install --global 'DatabaseMcpServer'
dotnet tool list --global
DatabaseMcpServer --help
DatabaseMcpServer tool list
DatabaseMcpServer tool help query_database
DatabaseMcpServer config help add
```

程序不支持 --version。全局安装和独立目录安装可能是不同版本，不能用一个可执行文件的帮助证明另一个支持某参数。

## 配置路径

| 入口 | 定位规则 |
| --- | --- |
| tool / -web | --config；否则当前目录 databases.json、local-databases.json、DB_CONFIG_PATH、用户目录/.database-mcp/databases.json |
| init / config | --config；否则用户目录/.database-mcp/databases.json |
| -web 首次启动 | 未找到现有配置时可启动，使用用户目录作为可写目标 |

显式 --config 或已选中的 DB_CONFIG_PATH 指向不存在文件时会报错，不应期待悄悄回退。数据库凭据由配置文件提供；不要用旧 DB_CONNECTION_STRING/DB_TYPE 启动方式替代当前配置契约。

## 初始化与管理

```powershell
DatabaseMcpServer init --config 'D:/config/databases.json'
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'Sqlite'
DatabaseMcpServer config add --name 'sqlite-local' --db-type 'Sqlite' `
  --connection-string 'Data Source=D:/data/local.db;Mode=ReadWriteCreate;' `
  --set-default --config 'D:/config/databases.json'
DatabaseMcpServer config validate --config 'D:/config/databases.json'
DatabaseMcpServer config doctor --test-connections false --config 'D:/config/databases.json'
DatabaseMcpServer config test --name 'sqlite-local' --config 'D:/config/databases.json'
```

config doctor 默认 test-connections=true，会连接数据库；离线检查须显式 false。config validate 和 get_database_capabilities 不连接数据库。

其他配置操作通过 config help 查询：list、show、create、rename、update、clone、remove、use/set-default、export、import。remove 要求 --yes；force 用于明确覆盖。export 可能包含凭据，不应直接放入公开日志或仓库。

连接中的 accessMode 可取 read-only、read-write、schema-admin；config add/update 没有 --access-mode 参数，需要通过 JSON 配置或当前实际支持的编辑入口设置，不要发明选项。enableDangerousOperations 默认为 false。

## 选库与部分成功

| 操作 | 作用范围 |
| --- | --- |
| config use --name X | 保存配置的 isDefault |
| tool switch_database --database-name X | 持久化 CLI 当前选择到用户目录/.database-mcp/cli-state.json，按配置绝对路径隔离 |
| 查询/写工具的 --database-name X | 固定本请求目标，不修改 isDefault 或 CLI 当前选择 |
| reload_database_config | 名称仍存在时保留当前连接 |

```powershell
DatabaseMcpServer tool get_current_database --config 'D:/config/databases.json'
DatabaseMcpServer tool list_databases --config 'D:/config/databases.json'
DatabaseMcpServer config show --name 'reporting' --config 'D:/config/databases.json'
```

显式切库的状态持久化失败会返回非零退出码并保留原选择；先检查结果再继续后续命令。按名执行不切换当前选择；启动时若持久化的旧名称已失效，会回退并修正该配置的选择状态。

配置编辑和重载分别报告 saved、runtimeApplied、selectionPersisted（字段依具体操作出现）。saved=true 且 success=false 可能表示文件已保存、运行时发布或选择持久化失败。重新读取实际配置，以返回的 databaseName 确定重试目标，不要盲目再次创建或重命名。

runtimeVersion / savedVersion 是当前进程观察的版本；hasPendingChanges=true 表示磁盘与运行快照不同，null 表示无法确认。损坏配置不会替换长驻进程最近有效快照。

## 选择查询工具

| 需求 | 当前入口 |
| --- | --- |
| 能力、驱动、执行预算 | get_database_capabilities |
| SQL 表结构 | get_table_schema，可用 refresh=true |
| 参数化只读 SQL | query_database，类型化参数数组 |
| 按表分页 | query_table_page，要求主键，默认 100 行，不隐式 COUNT |
| 单行、标量、多结果集 | sql_query_single、get_scalar、get_data_set_all |
| 1–5 条独立只读 SQL | batch_sql_query，逐项检查执行/失败/预算 |
| Mongo | mongo_list_collections、mongo_find、mongo_count |
| Mongo 写入 | mongo_insert_one、mongo_update、mongo_delete |

SQL/DDL 工具对 Mongo 返回 1012，Mongo 工具对关系型连接也返回 1012。缺失 provider 能力不等同连接失败。驱动已随包提供也不等同每种数据库都完成实库验收。

## 权限、预算与取消

DB_TOOL_ACCESS 与连接 accessMode 取更严格值。--yes 表示 CLI 写入确认，不能放宽权限；危险 SQL 和 Mongo 批量/空过滤写仍检查 enableDangerousOperations。

query_database 和 Mongo 默认 1000 行、1 MiB、30 秒；请求参数不能超过服务器 DB_QUERY_MAX_ROWS / DB_QUERY_MAX_BYTES / DB_COMMAND_MAX_TIMEOUT_SECONDS。0 秒仅在 DB_ALLOW_INFINITE_TIMEOUT=true 时允许。

- limitReached=true、truncated=null：碰到行数预算，没有探测下一条。
- truncated=true：返回被预算截断；不能当作完整数据。Mongo 首个文档过大时可返回空数组并标记截断。
- 响应字节预算不是数据库工作量、内存或完整协议字节限制。
- 每配置连接默认并发 8、排队 32、等待 30 秒；DB_MAX_CONCURRENT / DB_MAX_QUEUED / DB_QUEUE_TIMEOUT_SECONDS 可以调整。一次性 CLI 进程各自计数。
- Ctrl+C 取消退出 130；同步驱动调用可能需要等待返回，取消不会回滚已提交写入。

## 本地 Web 与监听

```powershell
DatabaseMcpServer -web --config 'D:/config/databases.json' --port 5129 --no-browser
DatabaseMcpServer -web --config 'D:/config/databases.json' --enable-monitor-config true
```

-web 仅监听 loopback。port 范围 0–65535，0 或省略可自动分配。写 API 要求 X-DatabaseMcp-Web: 1；存在 Origin 时必须匹配本地 scheme/host/port。工具写操作要求输入工具全名。Web JSON 参数按真实 JSON 类型绑定，整数/布尔值不应包成字符串。

监听只用于长驻进程；一次性 tool/config/init 不开启监听。优先级：启动 --enable-monitor-config → ENABLE_MONITOR_CONFIG → 配置根级 enableMonitorConfig → 默认关闭。监听重载跟随文件默认库；显式 reload_database_config 尽量保留当前选择。

## 输出与报告

帮助和 tool list 写 stderr，工具/配置结果写 stdout。退出码 0 成功、1 业务失败、2 用法/绑定/启动失败、130 取消。批量顶层成功仍需检查全部 results。get_database_config 没有顶层 success。

记录可执行文件来源、配置路径来源、目标连接、脱敏 argv、退出码、结果摘要和清理状态。仅报告实际验证过的 provider；没有运行的集成环境不得标成已通过。
