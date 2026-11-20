# DatabaseMcpServer CLI 命令文档

本文档面向直接在命令行调用 `DatabaseMcpServer` 的场景，不讨论 MCP 客户端接入。

---

## 1. 基本说明

`DatabaseMcpServer` 现在有两种运行模式：

- **无参数**：启动 stdio MCP Server
- **`-web`**：启动本地 Web 配置管理页
- **`tool` 子命令**：直接执行某个 MCP tool
- **`init` / `config` 子命令**：初始化和维护本地连接配置

CLI 模式的基本格式：

```powershell
DatabaseMcpServer [--enable-monitor-config true|false]
DatabaseMcpServer [--enable-monitor-config true|false] -web [--config path] [--port number] [--no-browser]
DatabaseMcpServer init [--config path] [--force]
DatabaseMcpServer config <subcommand> [options]
DatabaseMcpServer tool <tool_name> [--option value...]
```

示例：

```powershell
DatabaseMcpServer -web
DatabaseMcpServer -web --config 'D:\config\databases.json' --no-browser
DatabaseMcpServer --enable-monitor-config true
DatabaseMcpServer -web --enable-monitor-config false --no-browser
DatabaseMcpServer init
DatabaseMcpServer config list
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'Sqlite'
DatabaseMcpServer config create --from-preset 'Sqlite' --name 'sqlite-local' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' --description 'local sqlite' --set-default
DatabaseMcpServer config add --name 'sqlite-local' --db-type 'Sqlite' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' --set-default
DatabaseMcpServer config update --name 'sqlite-local' --enable-dangerous-operations true
DatabaseMcpServer config rename --name 'sqlite-local' --new-name 'sqlite-dev'
DatabaseMcpServer config update --name 'sqlite-dev' --description 'dev sqlite'
DatabaseMcpServer config validate
DatabaseMcpServer config clone --name 'sqlite-dev' --new-name 'sqlite-ci'
DatabaseMcpServer config doctor
DatabaseMcpServer config export --output '.\backup-databases.json'
DatabaseMcpServer config import --input '.\backup-databases.json' --config 'D:\config\databases.json' --force
DatabaseMcpServer tool list
DatabaseMcpServer tool help switch_database
DatabaseMcpServer tool test_connection --config 'D:\config\databases.json'
DatabaseMcpServer tool get_table_schema --table-name 'users' --config 'D:\config\databases.json'
```

### 1.1 `-web` 模式说明

`DatabaseMcpServer -web` 会在本机启动一个仅监听 `localhost / 127.0.0.1` 的配置管理站点，默认自动打开浏览器。

- 默认配置解析顺序与 CLI `tool` 模式一致：
  - `--config`
  - `./databases.json`
  - `./local-databases.json`
  - `DB_CONFIG_PATH`
  - `%USERPROFILE%/.database-mcp/databases.json`
- 如果上述位置都没有现成配置文件，`-web` 仍会启动，但会把 `%USERPROFILE%/.database-mcp/databases.json` 作为默认可写目标，方便先初始化再编辑。
- 页面同时管理两套状态：
  - `databases.json` 中的默认连接
  - `%USERPROFILE%/.database-mcp/cli-state.json` 中按 config 路径隔离的“当前连接”
- “连接管理”工作区支持按名称/类型/说明搜索，按数据库类型或默认/当前/危险/健康状态筛选，并按名称、类型、状态或健康延迟排序。
- “全部健康检查”会显式检查当前配置中的所有连接并显示汇总、逐项延迟和错误；页面加载时不会自动访问数据库。
- 新增/编辑连接可使用后端类型目录生成的连接字符串向导，也可切换到原始模式。编辑时默认保持现有连接字符串，页面和 API 都不会返回未脱敏的已保存值。
- 右上角可选择亮色、暗色或跟随系统主题，选择只保存在浏览器本地。
- “Tool Playground”调用的是当前进程内与 CLI `tool` 模式相同的已注册 Tool catalog，不是任意远程 MCP 客户端：
  - 参数和结果仅保存在当前页面内存中，不写入 `localStorage` 或配置文件；
  - 受保护 Tool 必须输入完整 Tool 名称，服务端仍以 `CliWriteProtection` 再次校验；
  - Tool 请求仅接受本地同源 JSON 请求，并限制为 1 MiB；
  - 调用响应中的 `queueMs` 是等待工具执行槽位的毫秒数，`durationMs` 是获得槽位后执行工具的毫秒数。
  - 关闭“等待响应”只停止浏览器等待，不代表同步数据库操作已经取消。
- 常用参数：
  - `--port <number>`：手工指定本地端口；必须在 `0-65535` 之间
  - `--no-browser`：只启动服务，不自动打开浏览器
  - `--enable-monitor-config true|false`：本进程强制开启或关闭 `databases.json` 监听，优先级高于环境变量和配置文件
- 对外网暴露不是这个模式的目标；如果用户想“让别人从另一台机器打开这个页面”，应明确说明当前实现是 localhost-only。

### 1.2 配置文件监听

长驻进程（无参数 stdio MCP / `-web`）可以监听 `databases.json`：文件里的默认库变化时，运行时当前库会跟着切换。一次性 `tool` / `config` / `init` 命令不会启用监听。

可以写在配置文件根级：

```json
{
  "enableMonitorConfig": true,
  "databases": []
}
```

优先级从高到低：

| 优先级 | 来源 | 说明 |
| --- | --- | --- |
| 1（最高） | `--enable-monitor-config true\|false` | 仅对本进程生效，强制开启或关闭；不写回配置文件 |
| 2 | 环境变量 `ENABLE_MONITOR_CONFIG` | `true`/`false`（也接受 `1`/`0`/`yes`/`no`/`on`/`off`）；未设置时继续往下看 |
| 3 | 配置文件 `enableMonitorConfig` | `databases.json` 根字段；可直接编辑 JSON |
| 4（默认） | 未配置 | 关闭监听 |

配置文件里的 `enableMonitorConfig` 是持久默认值。启动参数和环境变量只覆盖当前进程，不会改文件。

---

## 2. 用户使用流程

从零安装到日常使用的完整生命周期。每一步都给出最常用的命令，按顺序执行即可走通。

### 2.1 安装与升级

通过 .NET Global Tool 安装：

```powershell
dotnet tool install --global DatabaseMcpServer
dotnet tool list --global | Select-String databasemcpserver
```

**注意**：查安装版本用 `dotnet tool list --global`，不要用 `DatabaseMcpServer --version`——CLI 本身并不识别 `--version` 这个参数，会以退出码 `2` 返回 `未知命令: '--version'`。.NET 工具清单才是权威来源。

升级到最新版：

```powershell
dotnet tool update --global DatabaseMcpServer
```

如果命令找不到，确认 `%USERPROFILE%\.dotnet\tools` 在 `PATH` 中。

### 2.2 初始化默认配置

第一次使用，先生成默认配置文件：

```powershell
DatabaseMcpServer init
```

默认目标位置 `%USERPROFILE%/.database-mcp/databases.json`。如果想用自定义路径，加 `--config <path>`；如果文件已存在但想重置，加 `--force`。

CLI 和本地 Web 修改同一配置时，通过配置旁的 `.lock` 文件协调读改写。写入成功后一次替换完整 JSON。等待写锁超过 5 秒会返回可重试的错误。空锁文件会保留，程序运行时请保留它。编辑器和第三方脚本直接写文件时需要自行协调。

Linux 容器更新已有配置时，镜像须提供 GNU coreutils 的 `/bin/cp`，用于复制访问权限。配置目录需要允许创建锁文件和同目录临时文件。

### 2.3 添加第一个连接

可以先看下内置模板，再决定怎么填：

```powershell
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'MySql'
```

直接 `add`（手动指定字段）：

```powershell
DatabaseMcpServer config add `
  --name 'mysql-dev' `
  --db-type 'MySql' `
  --connection-string 'Server=127.0.0.1;Port=3306;Database=crm;User=root;Password=secret;SslMode=None;' `
  --description '本地 MySQL 开发库' `
  --set-default `
  --enable-dangerous-operations false
```

`--enable-dangerous-operations` 控制 `execute_command` 等通用命令是否允许执行 DDL/无 WHERE 更新；默认关闭，建议仅对受控维护连接开启。

或从模板创建（自动套用该 DB 类型的合理默认）：

```powershell
DatabaseMcpServer config create --from-preset 'MySql' `
  --name 'mysql-dev' `
  --connection-string 'Server=127.0.0.1;Port=3306;Database=crm;User=root;Password=secret;SslMode=None;' `
  --set-default
```

### 2.4 验证连接可用

```powershell
DatabaseMcpServer config validate                # 检查配置文件结构
DatabaseMcpServer config test --name 'mysql-dev' # 单连接连通性
DatabaseMcpServer config doctor                  # 全量诊断 + 修复建议
```

`config doctor` 会逐个测连接、列出问题并附修复建议，非常适合首跑做体检。

### 2.5 开始查询

养成「先看 schema，再写 SQL」的习惯：

```powershell
DatabaseMcpServer tool list_databases
DatabaseMcpServer tool get_current_database
DatabaseMcpServer tool get_table_info_list
DatabaseMcpServer tool get_table_schema --table-name 'users'
DatabaseMcpServer tool query_database --sql 'select * from users limit 10'
```

不带 `--config` 时，CLI 按 `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json` 顺序查找配置。

### 2.6 多连接日常切换

这一步最容易踩坑：CLI 有两个独立的状态层，对应两个不同命令。

| 命令 | 改动对象 | 用途 |
| --- | --- | --- |
| `config use --name X` | `databases.json` 的 `isDefault` | 永久改默认连接（写入文件） |
| `tool switch_database --database-name X` | `%USERPROFILE%/.database-mcp/cli-state.json` | 临时切当前连接（按 config 路径隔离），不改文件 |

后续每次 `tool` 调用会优先用 cli-state；只有当 cli-state 不存在或保存的连接已被删除时，才回退到 `isDefault`。

多个进程通过 `cli-state.json.lock` 协调选库记录的更新。显式切库遇到状态文件损坏、写入权限不足或等锁超时，会返回 `success=false` 和非零退出码，并保留原选择。解决报错后可重试。脚本应检查切库的退出码，再执行后续数据库命令。

```powershell
DatabaseMcpServer config use --name 'mysql-prod'                       # 改默认（持久写入 databases.json）
DatabaseMcpServer tool switch_database --database-name 'mysql-staging' # 临时切（仅写 cli-state.json）
DatabaseMcpServer tool get_current_database                            # 验证当前到底连的是哪个
```

### 2.7 写入与 DDL（必须 `--yes`）

凡是会改数据或改 schema 的命令，必须显式 `--yes`，否则 CLI 以退出码 `2` 拒绝执行：

```powershell
DatabaseMcpServer tool execute_command --sql 'update users set status=1 where id=1' --yes
DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_email' --column-name 'email' --yes
```

完整的高风险命令清单见 [§7](#7-高风险命令与---yes)。

### 2.8 多环境与团队协作

如果项目里需要一份 repo 内的连接清单，把现有用户级配置导出成项目本地文件：

```powershell
DatabaseMcpServer config export --output '.\local-databases.json'
```

之后在该项目目录下调用 `tool` 时，会自动按查找顺序优先读取 `./local-databases.json`，无需 `--config`。也可以用环境变量明确指定：

```powershell
$env:DB_CONFIG_PATH = 'D:\config\team-databases.json'
DatabaseMcpServer tool test_connection
```

### 2.9 排错速查

| 退出码 | 含义 | 第一动作 |
| --- | --- | --- |
| `0` | 调用成功 | 解析 stdout JSON |
| `1` | tool 返回失败（`success: false`） | 看 backend / 数据库 / 权限 |
| `2` | CLI 用法错（缺参、缺 `--yes`、未知命令、JSON 格式坏） | 修命令本身，别去查数据库 |

需要确认 CLI 当前到底用的哪个连接：

```powershell
DatabaseMcpServer tool get_current_database
DatabaseMcpServer config show --name '<expected>'
```

---

## 3. 全局规则

### 3.1 命令命名

- CLI 命令名与 MCP tool 名完全一致，使用 `snake_case`
- 参数名统一映射为 `kebab-case`

例如：

- `test_connection_by_name`
- `get_table_schema`
- `--database-name`
- `--initial-delay-ms`

### 3.2 全局选项

所有 CLI 命令都支持以下全局选项：

| 选项 | 说明 |
| --- | --- |
| `--config <path>` | 指定本次调用使用的 `databases.json` |
| `--yes` | 对写操作 / 高风险 schema tool 进行显式确认 |
| `--help` / `-h` | 显示帮助 |

### 3.3 输出约定

- **tool 结果**：输出到 `stdout`
- **帮助信息**：输出到 `stderr`
- **成功调用时**：CLI 不输出运行日志，只输出结果 JSON

### 3.4 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 调用成功 |
| `1` | tool 返回结构化失败结果（通常是 `success: false`） |
| `2` | 参数错误、缺少必填项、未知命令、缺少 `--yes` 等 CLI 使用错误 |

### 3.5 配置文件定位规则

- 对 `init` / `config` 命令：
  - 默认目标文件为 `%USERPROFILE%/.database-mcp/databases.json`
  - 传入 `--config` 时，始终优先使用显式路径
- 对 `tool` 命令：
  - 如果没有显式传 `--config`，CLI 按以下顺序查找配置文件：

    1. `./databases.json`
    2. `./local-databases.json`
    3. 环境变量 `DB_CONFIG_PATH`
    4. `%USERPROFILE%/.database-mcp/databases.json`

### 3.6 当前连接 vs 默认连接

- `config use` / `config set-default`
  - 修改 `databases.json` 中的默认连接
  - 适合初始化或长期维护默认目标库
- `tool switch_database`
  - 修改 CLI `tool` 模式下的当前连接
  - 会按"已解析后的 config 路径"持久化到 `%USERPROFILE%/.database-mcp/cli-state.json`
  - 不会改写 `databases.json` 中的默认连接
- `tool get_current_database` / `tool list_databases` / 其他直接调用 tool 的命令
  - 优先使用已持久化的当前连接
  - 只有在没有保存当前连接，或保存的连接已经不存在时，才回退到默认连接

---

## 4. 配置管理命令

### 4.1 查看内置模板

```powershell
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'Sqlite'
DatabaseMcpServer config preset --db-type 'SqlServer'
DatabaseMcpServer config create --from-preset 'Sqlite' --name 'sqlite-local'
DatabaseMcpServer config create --from-preset 'SqlServer' --name 'sqlserver-crm' --connection-string 'Server=localhost;Database=crm;User Id=sa;Password=123456;Encrypt=True;TrustServerCertificate=True;'
DatabaseMcpServer config create --from-preset 'Sqlite' --name 'sqlite-preview' --print-only
```

### 4.2 初始化默认配置文件

```powershell
DatabaseMcpServer init
DatabaseMcpServer init --config 'D:\config\databases.json'
DatabaseMcpServer init --config 'D:\config\databases.json' --force
```

### 4.3 列出连接

```powershell
DatabaseMcpServer config list
DatabaseMcpServer config list --config 'D:\config\databases.json'
```

### 4.4 新增连接

```powershell
DatabaseMcpServer config add `
  --name 'sqlite-local' `
  --db-type 'Sqlite' `
  --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' `
  --description '本地 SQLite 开发库' `
  --set-default `
  --enable-dangerous-operations false
```

`--enable-dangerous-operations true|false` 也可用于 `config create` 和 `config update`；开启后当前连接允许通用命令工具执行危险操作。

### 4.5 重命名 / 更新连接

```powershell
DatabaseMcpServer config rename --name 'sqlite-local' --new-name 'sqlite-dev'
DatabaseMcpServer config update --name 'sqlite-dev' --description '开发环境 SQLite' --set-default
DatabaseMcpServer config update --name 'sqlite-dev' --enable-dangerous-operations true
DatabaseMcpServer config update --name 'sqlite-dev' --clear-description
```

### 4.6 查看 / 测试 / 删除连接

```powershell
DatabaseMcpServer config show --name 'sqlite-local'
DatabaseMcpServer config test --name 'sqlite-local'
DatabaseMcpServer config use --name 'sqlite-local'   # 修改默认连接
DatabaseMcpServer tool switch_database --database-name 'sqlite-local' --config 'D:\config\databases.json'   # 修改当前连接（CLI 下可跨多次调用保留）
DatabaseMcpServer config remove --name 'sqlite-local' --yes
```

### 4.7 校验配置文件

```powershell
DatabaseMcpServer config validate
DatabaseMcpServer config validate --config 'D:\config\databases.json'
```

### 4.8 克隆 / 诊断连接

```powershell
DatabaseMcpServer config clone --name 'sqlite-dev' --new-name 'sqlite-ci'
DatabaseMcpServer config doctor
DatabaseMcpServer config doctor --test-connections false
DatabaseMcpServer config doctor --name 'sqlite-dev'
DatabaseMcpServer config doctor --fix-suggestions true
DatabaseMcpServer config doctor --summary-only
```

### 4.9 导出 / 导入配置文件

```powershell
DatabaseMcpServer config export --output '.\backup-databases.json'
DatabaseMcpServer config import --input '.\backup-databases.json' --config 'D:\config\databases.json' --force
```

---

## 5. 常用辅助命令

### 5.1 列出所有可用命令

```powershell
DatabaseMcpServer tool list
```

### 5.2 查看某个命令的帮助

```powershell
DatabaseMcpServer tool help get_table_schema
DatabaseMcpServer tool get_table_schema --help
DatabaseMcpServer config add --help
```

### 5.3 查看程序根帮助

```powershell
DatabaseMcpServer --help
```

---

## 6. PowerShell 传参建议

Windows PowerShell 下，SQL 和 JSON 参数建议优先用**单引号**包裹，避免转义混乱。

### 6.1 SQL 示例

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select top 10 [id],[name] from [dbo].[users] order by [id]' `
  --config 'D:\config\databases.json'
```

### 6.2 JSON 参数示例

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from users where age > @age and city = @city' `
  --parameters '[{"name":"age","value":18},{"name":"city","value":"北京"}]' `
  --config 'D:\config\databases.json'
```

### 6.3 JSON 数组示例

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from users where id in (@ids)' `
  --parameters '[{"name":"ids","value":[1,2,3]}]' `
  --config 'D:\config\databases.json'
```

### 6.4 命令超时示例

查询与数据操作类工具支持可选 `--command-timeout-seconds`：

- 省略：query_database 默认 30 秒；其他工具按执行策略配置，未启用默认预算时使用驱动默认（通常 300 秒）
- `0`：无限等待
- 合法范围：`0–86400`
- 批处理工具上的超时作用于每条命令

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from large_report where day = @day' `
  --parameters '[{"name":"day","value":"2026-08-01"}]' `
  --command-timeout-seconds 900 `
  --config 'D:\config\databases.json'

DatabaseMcpServer tool execute_command `
  --sql 'update large_table set status=@status where id=@id' `
  --parameters '[{"name":"status","value":"done"},{"name":"id","value":1}]' `
  --command-timeout-seconds 900 `
  --yes `
  --config 'D:\config\databases.json'
```

### 6.5 批量只读查询示例

`batch_sql_query` 一次接受 1-5 条 SQL，不需要 `--yes`，并逐条返回结果或错误：

```powershell
DatabaseMcpServer tool batch_sql_query `
  --queries '["select count(*) from users","select count(*) from roles"]' `
  --command-timeout-seconds 600 `
  --config 'D:\config\databases.json'
```

查询结果预算示例（PowerShell）：

```powershell
DatabaseMcpServer tool query_database `
  --sql 'select * from users' `
  --max-rows 1000 `
  --max-response-bytes 1048576 `
  --config 'D:\config\databases.json'
```

同样适用于 IN 查询、batch 和 SQLite/MySQL/PostgreSQL/SQL Server 多结果集。预算在 reader 读取过程中执行，不会先加载完整结果。batch 的预算在所有查询间共享，耗尽后剩余项标为 `executed=false`。`truncated=null` 表示触发行数限制，未额外读取下一行确认是否还有数据；完整边界与最小字节数见 [TOOLS.md](../TOOLS.md#查询6)。命名目标、类型化参数、分页和服务端限制见[数据库 MCP 工作流](database-mcp-workflow.md)。

配置路径在每个 Host 创建时固定，Web/CLI 不再为传递路径修改 `DB_CONFIG_PATH`。Web 写接口统一要求 `X-DatabaseMcp-Web: 1`；有 `Origin` 时必须匹配本地 scheme、host 和 port。该要求包括无请求体的 DELETE、set-default 和 multipart import。

配置编辑响应分别报告 `saved`、`runtimeApplied` 和涉及选库时的 `selectionPersisted`。已保存但运行时更新失败时 `success=false`，页面显示错误并刷新实际配置；初始化空配置会明确表示没有可用运行时。重命名当前连接会同步维护运行时和 CLI 持久化选择，且不会覆盖另一进程已更改的选择。

内部工具通过租约独占 SqlSugarClient，请求结束即释放，物理连接由驱动池管理。所有接口都服从准入限制。CLI JSON 输出和 Web 诊断不使用进程级 Console 重定向。

### 6.6 批量写命令示例

`batch_execute_commands` 不是事务；单条失败不会回滚之前成功的命令。调用方必须检查每个 `results[]`：

```powershell
DatabaseMcpServer tool batch_execute_commands `
  --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"delete from sessions where user_id=@id","parameters":[{"name":"id","value":1}]}]' `
  --command-timeout-seconds 900 `
  --yes `
  --config 'D:\config\databases.json'
```

---

## 7. 高风险命令与 `--yes`

以下命令必须显式追加 `--yes`：

- `execute_command`
- `call_stored_procedure`
- `execute_command_with_go`
- `batch_execute_commands`
- `create_table`
- `drop_table`
- `truncate_table`
- `backup_table`
- `rename_table`
- `add_column`
- `update_column`
- `drop_column`
- `rename_column`
- `add_primary_key`
- `drop_constraint`
- `create_index`
- `add_default_value`
- `add_table_remark`
- `delete_table_remark`
- `add_column_remark`
- `delete_column_remark`
- `drop_view`
- `drop_func`
- `drop_proc`

示例：

```powershell
DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'
```

---

## 8. 命令清单

每个 tool 的 MCP/CLI 参数、返回行为、风险等级和调用示例见 [TOOLS.md](../TOOLS.md)。本节保留按 CLI 工作流组织的速查表。

## 8.1 连接与配置

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `test_connection` | 测试当前连接 | `DatabaseMcpServer tool test_connection --config 'D:\config\databases.json'` |
| `test_connection_by_name` | 测试指定连接 | `DatabaseMcpServer tool test_connection_by_name --database-name 'reporting' --config 'D:\config\databases.json'` |
| `get_database_config` | 获取当前配置摘要 | `DatabaseMcpServer tool get_database_config --config 'D:\config\databases.json'` |
| `validate_configuration` | 验证配置 | `DatabaseMcpServer tool validate_configuration --config 'D:\config\databases.json'` |
| `reload_database_config` | 重新加载配置，并尽量保留当前连接 | `DatabaseMcpServer tool reload_database_config --config 'D:\config\databases.json'` |
| `list_databases` | 列出所有连接 | `DatabaseMcpServer tool list_databases --config 'D:\config\databases.json'` |
| `switch_database` | 切换并持久化当前连接（按 config 路径隔离） | `DatabaseMcpServer tool switch_database --database-name 'reporting' --config 'D:\config\databases.json'` |
| `get_current_database` | 查看当前连接（优先读取 CLI 持久化状态） | `DatabaseMcpServer tool get_current_database --config 'D:\config\databases.json'` |
| `get_database_capabilities` | 离线查看指定连接的 provider、驱动和操作能力 | `DatabaseMcpServer tool get_database_capabilities --database-name 'reporting' --config 'D:\config\databases.json'` |
| `health_check` | 健康检查 | `DatabaseMcpServer tool health_check --config 'D:\config\databases.json'` |
| `test_connection_with_retry` | 带重试测试连接 | `DatabaseMcpServer tool test_connection_with_retry --max-retries 3 --initial-delay-ms 1000 --config 'D:\config\databases.json'` |

## 8.2 架构查询

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `get_data_base_list` | 获取实例中的数据库列表 | `DatabaseMcpServer tool get_data_base_list --config 'D:\config\databases.json'` |
| `get_view_info_list` | 获取视图列表 | `DatabaseMcpServer tool get_view_info_list --config 'D:\config\databases.json'` |
| `get_table_info_list` | 获取表列表 | `DatabaseMcpServer tool get_table_info_list --config 'D:\config\databases.json'` |
| `get_column_infos_by_table_name` | 获取表字段信息 | `DatabaseMcpServer tool get_column_infos_by_table_name --table-name 'users' --config 'D:\config\databases.json'` |
| `get_is_identities` | 获取自增列 | `DatabaseMcpServer tool get_is_identities --table-name 'users' --config 'D:\config\databases.json'` |
| `get_primaries` | 获取主键 | `DatabaseMcpServer tool get_primaries --table-name 'users' --config 'D:\config\databases.json'` |
| `get_index_list` | 获取索引列表 | `DatabaseMcpServer tool get_index_list --table-name 'users' --config 'D:\config\databases.json'` |
| `get_proc_list` | 获取存储过程列表 | `DatabaseMcpServer tool get_proc_list --config 'D:\config\databases.json'` |
| `get_func_list` | 获取函数列表 | `DatabaseMcpServer tool get_func_list --config 'D:\config\databases.json'` |
| `get_trigger_names` | 获取触发器列表 | `DatabaseMcpServer tool get_trigger_names --table-name 'users' --config 'D:\config\databases.json'` |
| `get_table_schema` | 获取汇总表结构 | `DatabaseMcpServer tool get_table_schema --table-name 'users' --config 'D:\config\databases.json'` |

## 8.3 存在性检查

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `is_any_table` | 判断表是否存在 | `DatabaseMcpServer tool is_any_table --table-name 'users' --config 'D:\config\databases.json'` |
| `is_any_column` | 判断列是否存在 | `DatabaseMcpServer tool is_any_column --table-name 'users' --column-name 'email' --config 'D:\config\databases.json'` |
| `is_any_constraint` | 判断约束是否存在 | `DatabaseMcpServer tool is_any_constraint --constraint-name 'PK_users' --config 'D:\config\databases.json'` |
| `is_any_table_remark` | 判断表描述是否存在 | `DatabaseMcpServer tool is_any_table_remark --table-name 'users' --config 'D:\config\databases.json'` |

## 8.4 查询

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `query_database` | 执行只读查询（可选 `--command-timeout-seconds`） | `DatabaseMcpServer tool query_database --sql 'select top 10 * from users' --command-timeout-seconds 600 --config 'D:\config\databases.json'` |
| `query_table_page` | 按主键稳定分页，默认 100 行，不隐式 COUNT | `DatabaseMcpServer tool query_table_page --table-name 'users' --config 'D:\config\databases.json'` |
| `sql_query_single` | 查询单条记录 | `DatabaseMcpServer tool sql_query_single --sql 'select top 1 * from users order by id desc' --config 'D:\config\databases.json'` |
| `get_data_set_all` | 多结果集查询 | `DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --config 'D:\config\databases.json'` |
| `get_scalar` | 查询标量值 | `DatabaseMcpServer tool get_scalar --sql 'select count(*) from users' --config 'D:\config\databases.json'` |
| `batch_sql_query` | 顺序执行 1-5 条只读查询并逐条返回结果（超时作用于每条命令，行数/字节预算整批共享） | `DatabaseMcpServer tool batch_sql_query --queries '["select count(*) from users","select count(*) from roles"]' --command-timeout-seconds 600 --config 'D:\config\databases.json'` |

## 8.5 数据写入

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `execute_command` | 执行 DML（可选 `--command-timeout-seconds`） | `DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where id=1' --command-timeout-seconds 900 --yes --config 'D:\config\databases.json'` |
| `batch_execute_commands` | 非事务批量执行命令对象，每项可带类型化 parameters；超时作用于每条命令 | `DatabaseMcpServer tool batch_execute_commands --commands '[{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"active"},{"name":"id","value":1}]},{"sql":"update users set status=@status where id=@id","parameters":[{"name":"status","value":"inactive"},{"name":"id","value":2}]}]' --command-timeout-seconds 900 --yes --config 'D:\config\databases.json'` |
| `call_stored_procedure` | 调用存储过程 | `DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_monthly_report' --parameters '[{"name":"year","value":2025},{"name":"month","value":11}]' --yes --config 'D:\config\databases.json'` |
| `execute_command_with_go` | 执行带 GO 的 SQL Server 脚本 | 使用下方 here-string 示例，保留实际换行 |

```powershell
$sql = @'
UPDATE users SET status='active' WHERE id=1
GO
UPDATE users SET status='inactive' WHERE id=2
'@
DatabaseMcpServer tool execute_command_with_go --sql $sql --yes --config 'D:\config\databases.json'
```

## 8.6 架构变更

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `create_table` | 创建表 | `DatabaseMcpServer tool create_table --table-name 'users' --columns-info '[{"DbColumnName":"id","DataType":"int","IsPrimarykey":true,"IsIdentity":true},{"DbColumnName":"name","DataType":"nvarchar","Length":100}]' --yes --config 'D:\config\databases.json'` |
| `drop_table` | 删除表 | `DatabaseMcpServer tool drop_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'` |
| `truncate_table` | 清空表 | `DatabaseMcpServer tool truncate_table --table-name 'temp_users' --yes --config 'D:\config\databases.json'` |
| `backup_table` | 备份表 | `DatabaseMcpServer tool backup_table --old-table-name 'users' --new-table-name 'users_backup' --yes --config 'D:\config\databases.json'` |
| `rename_table` | 重命名表 | `DatabaseMcpServer tool rename_table --old-table-name 'users_tmp' --new-table-name 'users_archive' --yes --config 'D:\config\databases.json'` |
| `add_column` | 添加列 | `DatabaseMcpServer tool add_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":20,"IsNullable":true}' --yes --config 'D:\config\databases.json'` |
| `update_column` | 修改列 | `DatabaseMcpServer tool update_column --table-name 'users' --column-info '{"DbColumnName":"mobile","DataType":"nvarchar","Length":30,"IsNullable":true}' --yes --config 'D:\config\databases.json'` |
| `drop_column` | 删除列 | `DatabaseMcpServer tool drop_column --table-name 'users' --column-name 'mobile' --yes --config 'D:\config\databases.json'` |
| `rename_column` | 重命名列 | `DatabaseMcpServer tool rename_column --table-name 'users' --old-column-name 'mobile' --new-column-name 'phone' --yes --config 'D:\config\databases.json'` |
| `add_primary_key` | 添加主键 | `DatabaseMcpServer tool add_primary_key --table-name 'users' --column-name 'id' --yes --config 'D:\config\databases.json'` |
| `drop_constraint` | 删除约束 | `DatabaseMcpServer tool drop_constraint --table-name 'users' --constraint-name 'PK_users' --yes --config 'D:\config\databases.json'` |
| `create_index` | 创建索引 | `DatabaseMcpServer tool create_index --table-name 'users' --index-name 'IX_users_email' --column-name 'email' --yes --config 'D:\config\databases.json'` |
| `add_default_value` | 添加默认值 | `DatabaseMcpServer tool add_default_value --table-name 'users' --column-name 'status' --default-value '''active''' --yes --config 'D:\config\databases.json'` |
| `add_table_remark` | 添加表描述 | `DatabaseMcpServer tool add_table_remark --table-name 'users' --description '用户表' --yes --config 'D:\config\databases.json'` |
| `delete_table_remark` | 删除表描述 | `DatabaseMcpServer tool delete_table_remark --table-name 'users' --yes --config 'D:\config\databases.json'` |
| `add_column_remark` | 添加列描述 | `DatabaseMcpServer tool add_column_remark --table-name 'users' --column-name 'email' --description '邮箱地址' --yes --config 'D:\config\databases.json'` |
| `delete_column_remark` | 删除列描述 | `DatabaseMcpServer tool delete_column_remark --table-name 'users' --column-name 'email' --yes --config 'D:\config\databases.json'` |
| `drop_view` | 删除视图 | `DatabaseMcpServer tool drop_view --view-name 'v_active_users' --yes --config 'D:\config\databases.json'` |
| `drop_func` | 删除函数 | `DatabaseMcpServer tool drop_func --function-name 'fn_calc_score' --yes --config 'D:\config\databases.json'` |
| `drop_proc` | 删除存储过程 | `DatabaseMcpServer tool drop_proc --procedure-name 'sp_cleanup_logs' --yes --config 'D:\config\databases.json'` |

## 8.7 Mongo 集合

Mongo 使用原生集合工具；SQL/DDL 工具不适用。URI 必须包含业务数据库，`authSource` 不能替代目标库。对象参数支持 JSON / Extended JSON v2，输出使用 Canonical Extended JSON v2。

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `mongo_list_collections` | 列出集合 | `DatabaseMcpServer tool mongo_list_collections --database-name 'mongo-main' --config 'D:\config\databases.json'` |
| `mongo_find` | 有预算限制的集合查询 | `DatabaseMcpServer tool mongo_find --collection 'orders' --filter '{"status":"paid"}' --max-rows 20 --database-name 'mongo-main' --config 'D:\config\databases.json'` |
| `mongo_count` | 统计匹配文档 | `DatabaseMcpServer tool mongo_count --collection 'orders' --filter '{"status":"paid"}' --database-name 'mongo-main' --config 'D:\config\databases.json'` |
| `mongo_insert_one` | 插入单个文档 | `DatabaseMcpServer tool mongo_insert_one --collection 'orders' --document '{"_id":"order-42","total":{"$numberDecimal":"12.50"}}' --database-name 'mongo-main' --config 'D:\config\databases.json' --yes` |
| `mongo_update` | 默认更新一个匹配文档 | `DatabaseMcpServer tool mongo_update --collection 'orders' --filter '{"_id":"order-42"}' --update '{"$set":{"status":"paid"}}' --database-name 'mongo-main' --config 'D:\config\databases.json' --yes` |
| `mongo_delete` | 默认删除一个匹配文档 | `DatabaseMcpServer tool mongo_delete --collection 'orders' --filter '{"_id":"order-42"}' --database-name 'mongo-main' --config 'D:\config\databases.json' --yes` |

`many=true` 要求连接开启危险操作；空过滤还需 `allowAll=true`，包括单文档操作。`--yes` 不覆盖服务端或连接权限。写入响应为 `writeOutcome=unknown` 或取消后，先核实数据库实际状态，不盲目重试。完整约束见 [Mongo 集合契约](mongodb-collections.md)。

## 9. 推荐调用顺序

### 9.1 看连接

```powershell
DatabaseMcpServer tool validate_configuration --config 'D:\config\databases.json'
DatabaseMcpServer tool test_connection --config 'D:\config\databases.json'
DatabaseMcpServer tool get_database_config --config 'D:\config\databases.json'
```

### 9.2 看表结构

```powershell
DatabaseMcpServer tool is_any_table --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_table_schema --table-name 'users' --config 'D:\config\databases.json'
DatabaseMcpServer tool get_index_list --table-name 'users' --config 'D:\config\databases.json'
```

### 9.3 先查再改

```powershell
DatabaseMcpServer tool query_database --sql 'select top 20 * from users where status=''inactive''' --config 'D:\config\databases.json'
DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where status=''inactive''' --yes --config 'D:\config\databases.json'
```

---

## 10. 相关文档

### 执行与配置更新补充

- `tool` 查询及普通/批量写命令支持 Ctrl+C，取消退出码为 `130`。同步 schema、存储过程、GO 和完整多结果集路径仍需等待驱动返回；不承诺立即中断。
- 每配置连接默认 `8` 个执行、`32` 个排队、`30` 秒排队超时。可在启动前设置 `DB_MAX_CONCURRENT`、`DB_MAX_QUEUED`、`DB_QUEUE_TIMEOUT_SECONDS`；队满/超时返回 `1009`。不同 CLI 进程各自计数，不是跨进程数据库总限额。
- Web 编辑把重命名与属性修改合为一次 `PUT /api/databases/{原名}`（可选 `newName`），先校验完整候选再保存一次。旧 rename 接口仍保留。
- 响应的 `saved`、`runtimeApplied`、`selectionPersisted` 分别表示配置保存、当前进程应用和选择状态保存；不是跨文件事务。`databaseName` 表示已保存的实际名称，部分成功后重试使用这个名称。
- Dashboard 的 `currentDatabase` 来自实际运行时。`runtime.runtimeVersion` / `savedVersion` 是本进程观察到的单调版本号；`hasPendingChanges=true` 表示磁盘内容与运行快照不同，`null` 表示无法确认。版本号不包含连接串或内容哈希。配置被删除/损坏后，已有运行时保留最近有效目标。
- CLI JSON 输出和 Web 诊断不再重定向进程级 `Console.Out/Error`。诊断验证保存的候选配置，不会因旧运行时连接正常而误报成功。

- [README.md](../README.md)
- [TOOLS.md](../TOOLS.md)
- [DatabaseSetting/README.md](../DatabaseSetting/README.md)
- [执行能力、性能与限制](execution-optimization.md)
