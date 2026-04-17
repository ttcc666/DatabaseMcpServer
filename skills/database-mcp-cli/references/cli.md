# DatabaseMcpServer CLI 命令文档

本文档面向直接在命令行调用 `DatabaseMcpServer` 的场景，不讨论 MCP 客户端接入。

---

## 1. 基本说明

`DatabaseMcpServer` 现在有两种运行模式：

- **无参数**：启动 stdio MCP Server
- **`tool` 子命令**：直接执行某个 MCP tool
- **`init` / `config` 子命令**：初始化和维护本地连接配置

CLI 模式的基本格式：

```powershell
DatabaseMcpServer init [--config path] [--force]
DatabaseMcpServer config <subcommand> [options]
DatabaseMcpServer tool <tool_name> [--option value...]
```

示例：

```powershell
DatabaseMcpServer init
DatabaseMcpServer config list
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type 'Sqlite'
DatabaseMcpServer config create --from-preset 'Sqlite' --name 'sqlite-local' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' --description 'local sqlite' --set-default
DatabaseMcpServer config add --name 'sqlite-local' --db-type 'Sqlite' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' --set-default
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

---

## 2. 用户使用流程

从零安装到日常使用的完整生命周期。每一步都给出最常用的命令，按顺序执行即可走通。

### 2.1 安装与升级

通过 .NET Global Tool 安装：

```powershell
dotnet tool install --global DatabaseMcpServer
DatabaseMcpServer --version
```

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
  --set-default
```

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
DatabaseMcpServer tool sql_query --sql 'select * from users limit 10'
```

不带 `--config` 时，CLI 按 `./databases.json → ./local-databases.json → DB_CONFIG_PATH → %USERPROFILE%/.database-mcp/databases.json` 顺序查找配置。

### 2.6 多连接日常切换

这一步最容易踩坑：CLI 有两个独立的状态层，对应两个不同命令。

| 命令 | 改动对象 | 用途 |
| --- | --- | --- |
| `config use --name X` | `databases.json` 的 `isDefault` | 永久改默认连接（写入文件） |
| `tool switch_database --database-name X` | `%USERPROFILE%/.database-mcp/cli-state.json` | 临时切当前连接（按 config 路径隔离），不改文件 |

后续每次 `tool` 调用会优先用 cli-state；只有当 cli-state 不存在或保存的连接已被删除时，才回退到 `isDefault`。

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

### 2.8 导出与文档化

```powershell
DatabaseMcpServer tool export_query_to_excel `
  --sql 'select * from users' `
  --file-path 'D:\exports\users.xlsx'

DatabaseMcpServer tool generate_database_documentation `
  --format 'markdown' `
  --return-mode 'path' `
  --file-path 'D:\exports\db-doc.md'
```

### 2.9 多环境与团队协作

如果项目里需要一份 repo 内的连接清单，把现有用户级配置导出成项目本地文件：

```powershell
DatabaseMcpServer config export --output '.\local-databases.json'
```

之后在该项目目录下调用 `tool` 时，会自动按查找顺序优先读取 `./local-databases.json`，无需 `--config`。也可以用环境变量明确指定：

```powershell
$env:DB_CONFIG_PATH = 'D:\config\team-databases.json'
DatabaseMcpServer tool test_connection
```

### 2.10 排错速查

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
  --set-default
```

### 4.5 重命名 / 更新连接

```powershell
DatabaseMcpServer config rename --name 'sqlite-local' --new-name 'sqlite-dev'
DatabaseMcpServer config update --name 'sqlite-dev' --description '开发环境 SQLite' --set-default
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
DatabaseMcpServer tool sql_query `
  --sql 'select top 10 [id],[name] from [dbo].[users] order by [id]' `
  --config 'D:\config\databases.json'
```

### 6.2 JSON 参数示例

```powershell
DatabaseMcpServer tool sql_query `
  --sql 'select * from users where age > @age and city = @city' `
  --parameters '{"age":18,"city":"北京"}' `
  --config 'D:\config\databases.json'
```

### 6.3 JSON 数组示例

```powershell
DatabaseMcpServer tool sql_query_with_in_parameter `
  --sql 'select * from users where id in (@ids)' `
  --in-parameter-name 'ids' `
  --in-values '[1,2,3]' `
  --config 'D:\config\databases.json'
```

### 6.4 批量命令示例

```powershell
DatabaseMcpServer tool batch_execute_commands `
  --commands '["update users set status=''active'' where id=1","update users set status=''inactive'' where id=2"]' `
  --yes `
  --config 'D:\config\databases.json'
```

---

## 7. 高风险命令与 `--yes`

以下命令必须显式追加 `--yes`：

- `execute_command`
- `call_stored_procedure`
- `call_stored_procedure_with_output`
- `execute_command_with_go`
- `batch_execute_commands`
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
| `sql_query` | 执行只读查询 | `DatabaseMcpServer tool sql_query --sql 'select top 10 * from users' --config 'D:\config\databases.json'` |
| `sql_query_single` | 查询单条记录 | `DatabaseMcpServer tool sql_query_single --sql 'select top 1 * from users order by id desc' --config 'D:\config\databases.json'` |
| `get_data_set_all` | 多结果集查询 | `DatabaseMcpServer tool get_data_set_all --sql 'select * from users; select * from roles' --config 'D:\config\databases.json'` |
| `get_scalar` | 查询标量值 | `DatabaseMcpServer tool get_scalar --sql 'select count(*) from users' --config 'D:\config\databases.json'` |
| `sql_query_with_in_parameter` | 带 IN 参数查询 | `DatabaseMcpServer tool sql_query_with_in_parameter --sql 'select * from users where id in (@ids)' --in-parameter-name 'ids' --in-values '[1,2,3]' --config 'D:\config\databases.json'` |

## 8.5 数据写入

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `execute_command` | 执行 DML | `DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where id=1' --yes --config 'D:\config\databases.json'` |
| `batch_execute_commands` | 批量执行 DML | `DatabaseMcpServer tool batch_execute_commands --commands '["update users set status=''active'' where id=1","update users set status=''inactive'' where id=2"]' --yes --config 'D:\config\databases.json'` |
| `call_stored_procedure` | 调用存储过程 | `DatabaseMcpServer tool call_stored_procedure --procedure-name 'sp_monthly_report' --parameters '{"year":2025,"month":11}' --yes --config 'D:\config\databases.json'` |
| `call_stored_procedure_with_output` | 调用带输出参数的存储过程 | `DatabaseMcpServer tool call_stored_procedure_with_output --procedure-name 'sp_user_statistics' --input-parameters '{"UserId":1001}' --output-parameters '["TotalOrders"]' --yes --config 'D:\config\databases.json'` |
| `execute_command_with_go` | 执行带 GO 的 SQL Server 脚本 | `DatabaseMcpServer tool execute_command_with_go --sql \"UPDATE users SET status='active' WHERE id=1`nGO`nUPDATE users SET status='inactive' WHERE id=2\" --yes --config 'D:\config\databases.json'` |

## 8.6 架构变更

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
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

## 8.7 导出

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `export_query_to_excel` | 查询结果导出为 Excel | `DatabaseMcpServer tool export_query_to_excel --sql 'select * from users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'` |
| `export_table_to_excel` | 整表导出为 Excel | `DatabaseMcpServer tool export_table_to_excel --table-name 'users' --file-path 'D:\exports\users.xlsx' --return-format 'path' --config 'D:\config\databases.json'` |
| `export_multiple_queries_to_excel` | 多查询导出为多工作表 Excel | `DatabaseMcpServer tool export_multiple_queries_to_excel --queries-json '{"users":"select * from users","roles":"select * from roles"}' --file-path 'D:\exports\multi.xlsx' --return-format 'path' --config 'D:\config\databases.json'` |

## 8.8 文档生成

| CLI 命令 | 说明 | 示例 |
| --- | --- | --- |
| `generate_database_documentation` | 生成数据库文档 | `DatabaseMcpServer tool generate_database_documentation --format 'markdown' --return-mode 'path' --file-path 'D:\exports\db-doc.md' --config 'D:\config\databases.json'` |

---

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
DatabaseMcpServer tool sql_query --sql 'select top 20 * from users where status=''inactive''' --config 'D:\config\databases.json'
DatabaseMcpServer tool execute_command --sql 'update users set status=''active'' where status=''inactive''' --yes --config 'D:\config\databases.json'
```

---

## 10. 相关文档

- [README.md](../README.md)
- [TOOLS.md](../TOOLS.md)
- [DatabaseSetting/README.md](../DatabaseSetting/README.md)
