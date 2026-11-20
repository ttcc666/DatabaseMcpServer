---
name: database-mcp-cli
description: "使用 DatabaseMcpServer CLI（tool、config、init、-web）管理连接、执行 SQL 或 MongoDB 查询与写入、编写调用脚本及排查错误。适用于通过命令行操作数据库，不用于开发服务器源码或配置 MCP stdio 客户端。"
---

# DatabaseMcpServer CLI

帮助调用者选择工具、构造参数、执行数据库操作并判断结果。依据实际可执行文件的帮助，保留用户指定的连接、权限和操作范围。

## 入口与版本

- 命令入口：`DatabaseMcpServer`；用 `dotnet tool list --global` 查看安装版本，`--tool-path` 安装则查询对应目录。程序没有 `--version`，该参数会退出 2。
- 工具名称和参数以实际运行的 `tool list` / `tool help <name>` 为准。不同安装版本可能提供不同工具，不根据文档中的固定数量判断能力。

## 调用流程

1. 用 `tool list`、`tool help <name>`、`config help <subcommand>` 确认当前名称和参数。
2. 确定可执行文件与配置文件。脚本优先显式 `--config`；执行工具时优先使用支持的 `--database-name`，它不改变当前选择。
3. 查询 `list_databases` / `get_current_database`；离线能力用 `get_database_capabilities`。只有需要连通性证据时才执行 `test_connection_by_name` 或 `config test`。
4. SQL 的表结构未知时先用 `get_table_schema`，普通查询用 `query_database`，按表分页用 `query_table_page`；Mongo 用 `mongo_list_collections` / `mongo_find`，不发送 SQL。已有可靠的目标和结构信息时直接复用。
5. 已获用户授权的写入按帮助附加 `--yes`，同时遵守服务端及连接权限。
6. 同时检查退出码、JSON、逐项结果、截断标志和写入确认状态。报告实际执行结果；日志中的连接串、密码和敏感参数需脱敏。

## 当前契约

- SQL 参数统一为 `[{name,value,dbType?,direction?,size?,precision?,scale?}]`。NULL 必须指定 dbType；IN 数组通过 value 传递且不能声明 dbType；输出方向只用于存储过程，字符串/二进制输出需要 size。
- `batch_sql_query` 接受 1–5 条只读 SQL 字符串；`batch_execute_commands` 接受 `[{sql,parameters?}]`。批量写不是事务，失败或取消不会回滚已完成的写入。
- 批量工具的 `commandTimeoutSeconds` 应用于每条数据库命令，不是整批总截止时间。逐项检查 `results[]`，顶层成功和退出 0 不代表全部成功。
- 使用当前工具说明：旧 `sql_query`、`sql_query_with_in`、`get_table_data` 已移除；没有字典式 SQL 参数、`--parameters-array` 或分离的存储过程输入/输出参数入口。
- `query_database` 默认 1000 行、1 MiB、30 秒，字节预算至少 1024；`query_table_page` 默认 100 行。服务端上限仍生效。`limitReached=true,truncated=null` 表示触及行数限制，不能证明有下一页或已经读完。
- `DB_TOOL_ACCESS` 与连接 `accessMode` 取更严格权限。read-only 禁止写；read-write 禁止 DDL、schema 变更和存储过程执行。CLI 的 `--yes` 不覆盖权限或 `enableDangerousOperations`。
- Mongo 仅使用六个 `mongo_*` 集合工具，文档输入为 JSON / Extended JSON v2 对象。写入 `writeOutcome=unknown` 或取消后先核实实际状态，不自动重放。

## 配置与状态

| 对象 | 行为 |
| --- | --- |
| `config use` / `config set-default` | 修改配置文件 isDefault |
| `tool switch_database` | 按配置路径持久化 CLI 当前选择到用户目录的 cli-state.json |
| 工具的 `--database-name` | 只指定本次请求目标；错误名称不会回退到当前库 |
| `reload_database_config` | 当前名字仍存在时保留选择 |
| 编辑响应 | 分别检查 saved、runtimeApplied、selectionPersisted，不能把保存成功当成全部成功 |

`tool` / `-web` 查找顺序：`--config → ./databases.json → ./local-databases.json → DB_CONFIG_PATH → 用户目录/.database-mcp/databases.json`。`init` / `config` 默认写用户目录，需用 `--config` 指定其他路径。测试使用临时配置和临时对象。

## 输出与取消

| 退出码 / 输出 | 处理 |
| --- | --- |
| 0 | 调用成功；仍检查批量子项和结果边界 |
| 1 | 工具或配置操作返回失败；检查 errorCode、部分保存或写入状态 |
| 2 | 用法、参数绑定、配置路径或启动失败；先读 stderr |
| 130 | 操作取消；不代表写入已回滚 |

帮助和目录输出到 stderr；执行结果输出到 stdout。`get_database_config` 没有顶层 success，不能据此判失败。

## 按需阅读

| 任务 | 参考 |
| --- | --- |
| 配置定位、选库、监听、状态与权限 | [CLI 工作流](references/cli.md) |
| SQL 参数、批量写、GO、Mongo 和 PowerShell 示例 | [命令示例](references/commands.md) |
| 退出码、旧参数、超时、部分成功、驱动与 TLS | [故障排查](references/troubleshooting.md) |

这些参考随技能独立分发，无需项目源码即可使用；完整工具目录以实际 `tool list/help` 为准。
