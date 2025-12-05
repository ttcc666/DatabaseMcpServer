# DatabaseMCP 数据库操作服务器

[![NuGet](https://img.shields.io/nuget/v/DatabaseMcpServer.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![.NET Tool](https://img.shields.io/badge/.NET%20Tool-2.0.1-test-blue.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[🇺🇸 English](README_EN.md) | [🇨🇳 中文](README.md) | [🌐 官网](https://databasemcp.ttcc.online/)

 一个功能强大的数据库操作 MCP (Model Context Protocol) 服务器，支持 **34 种数据库类型**，**单实例多数据库动态切换**，让 AI 助手能够安全、便捷地执行数据库操作。

## ✨ 核心特性

- 🗄️ **多数据库支持** - 支持 34 种数据库类型（主流、国产、分布式、时序）
- 🔄 **单实例多数据库** - 一个 MCP Server 实例可配置和动态切换多个数据库连接
- 🔒 **安全防护** - 危险操作检测 + SQL 注入防护 + 敏感信息保护
- ⚡ **高性能优化** - SqlSugarScope 连接池复用 + 数据库特定优化 + 自动性能调优
- 🔧 **灵活配置** - 支持 JSON 配置文件，轻松管理多数据库连接
- 💾 **完整功能** - 55+ MCP 工具，涵盖查询、操作、架构管理、健康检查等
- 🚀 **生产就绪** - 支持事务、批量操作、存储过程、自动重连
- 📦 **.NET Global Tool** - 简单安装，一键部署
- 🌐 **跨平台** - Windows、macOS、Linux 全面支持

## 🗄️ 支持的数据库类型

### 🌐 主流数据库
- **MySQL** (默认)
- **SQL Server**
- **SQLite**
- **PostgreSQL**
- **Oracle**

### 🇨🇳 国产数据库
- **达梦数据库** (dm)
- **人大金仓** (kdbndp/kingbase)
- **神通数据库** (oscar)
- **瀚高数据库** (hg)
- **南大通用 GBase** (gbase)
- **虚谷数据库** (xugu)
- **海量数据库** (vastbase)
- **GoldenDB** (goldendb)

### 🚀 分布式数据库
- **OceanBase** (oceanbase)
- **TiDB** (tidb)
- **PolarDB** (polardb)
- **Doris** (doris)

### ⏱️ 时序数据库
- **TDengine** (tdengine)
- **QuestDB** (questdb)
- **ClickHouse** (clickhouse)

### 🔍 其他数据库
**分析型**：DuckDB、DuckDB
**接口**：Microsoft Access、ODBC
**企业**：SAP HANA、IBM DB2
**文档**：MongoDB
**专用**：OpenGauss、GaussDB 等

## 🚀 快速开始

### 第一步：安装 .NET Global Tool

```bash
# 安装最新版本
dotnet tool install --global DatabaseMcpServer

# 验证安装
DatabaseMcpServer --version
```

### 第二步：创建数据库配置文件

创建 `databases.json` 配置文件：

```json
{
  "databases": [
    {
      "name": "default",
      "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
      "dbType": "MySql",
      "description": "默认数据库",
      "isDefault": true
    }
  ]
}
```

### 第三步：配置 MCP 客户端

创建 `mcp.json` 配置文件（VS Code: `.vscode/mcp.json`）:

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### 第四步：测试连接并执行查询

重启 IDE 后，在 AI 助手中测试：

```
"测试数据库连接"
```

系统返回：
```json
{
  "success": true,
  "connected": true,
  "databaseType": "MySql"
}
```

## 📦 安装方式

### 方式 1：.NET Global Tool（推荐）

**安装**：
```bash
dotnet tool install --global DatabaseMcpServer
# 更新：dotnet tool update --global DatabaseMcpServer
```

**MCP 配置**：
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### 方式 2：dnx 命令

**安装**：
```bash
dnx DatabaseMcpServer@2.0.1-test --yes
```

**MCP 配置**：
```json
{
  "mcpServers": {
    "database": {
      "command": "dnx",
      "args": ["DatabaseMcpServer@2.0.1-test", "--yes"],
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### 方式 3：本地源码运行

**运行**：
```bash
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer
dotnet run
```

**MCP 配置**：
```json
{
  "mcpServers": {
    "database": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/DatabaseMcpServer"],
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

## ⚙️ 配置指南

DatabaseMcpServer 2.0.1-test 统一使用 JSON 配置文件管理数据库连接。

### 配置文件方式（必需）

通过环境变量 `DB_CONFIG_PATH` 指定配置文件的**绝对路径**：

**MCP 配置示例：**

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

**配置文件格式 (databases.json)：**

```json
{
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "Server=localhost;Database=myapp;User=root;Password=123456;",
      "dbType": "MySql",
      "description": "MySQL 主库",
      "isDefault": true,
      "optimizationSettings": {
        "enableCache": "true",
        "batchSize": "1000"
      }
    },
    {
      "name": "postgres-analytics",
      "connectionString": "Host=localhost;Database=analytics;Username=postgres;Password=123456;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL 分析库",
      "optimizationSettings": {
        "autoToLower": "true",
        "enableIlike": "true"
      }
    }
  ]
}
```

**多数据库管理工具：**
- `list_databases` - 列出所有可用的数据库连接
- `switch_database` - 切换到指定的数据库
- `get_current_database` - 获取当前活动的数据库
- `test_connection_by_name` - 测试指定数据库的连接

**性能优化工具：**
- `health_check` - 对所有数据库连接执行健康检查（响应时间、连接状态）
- `test_connection_with_retry` - 带自动重试的连接测试（指数退避策略）

---

## 🌐 环境配置

### 必需环境变量
- `DB_CONFIG_PATH`: 数据库配置文件路径（必需）
  - 示例: `D:\config\databases.json`

### 可选环境变量
- `SEQ_SERVER_URL`: Seq 日志服务器地址（可选）
- `SEQ_API_KEY`: Seq API 密钥（可选）
- `DB_DDL_WHITELIST`: DDL 操作白名单（可选，分号分隔的正则表达式）

### 数据库特定优化配置
从 2.0.1-test 版本开始，所有数据库特定优化配置都在 `databases.json` 的 `optimizationSettings` 中设置。

**详细配置文档**：
- [MySQL 配置指南](DatabaseSetting/MySQL.md)
- [SQL Server 配置指南](DatabaseSetting/SQLServer.md)
- [Oracle 配置指南](DatabaseSetting/Oracle.md)
- [PostgreSQL 配置指南](DatabaseSetting/PostgreSQL.md)
- [SQLite 配置指南](DatabaseSetting/SQLite.md)
- [达梦数据库配置指南](DatabaseSetting/DM.md)
- [人大金仓配置指南](DatabaseSetting/Kdbndp.md)
- [GaussDB 配置指南](DatabaseSetting/GaussDB.md)
- [QuestDB 配置指南](DatabaseSetting/QuestDB.md)
- [配置索引](DatabaseSetting/README.md)

---

## 🔄 从 1.x 迁移到 2.0

### ⚠️ 破坏性变更

DatabaseMcpServer 2.0.1-test 移除了环境变量配置方式，统一使用 JSON 配置文件。

### 迁移步骤

#### 1. 单数据库配置迁移

**旧方式（1.x - 已废弃）**:
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;...",
        "DB_TYPE": "MySql",
        "DB_DM_LOWERCASE_TABLES": "true"
      }
    }
  }
}
```

**新方式（2.0）**:

1. 创建 `databases.json` 文件：
```json
{
  "databases": [
    {
      "name": "default",
      "connectionString": "Server=localhost;Database=test;...",
      "dbType": "MySql",
      "description": "默认数据库",
      "isDefault": true,
      "optimizationSettings": {
        "lowercaseTables": "true"
      }
    }
  ]
}
```

2. 更新 MCP 配置：
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

#### 2. 环境变量映射表

| 旧环境变量 | 新 JSON 配置路径 |
|-----------|----------------|
| `DB_CONNECTION_STRING` | `databases[].connectionString` |
| `DB_TYPE` | `databases[].dbType` |
| `DB_DM_LOWERCASE_TABLES` | `databases[].optimizationSettings.lowercaseTables` |
| `DB_KDBNDP_MODE` | `databases[].optimizationSettings.mode` |
| `DB_GAUSSDB_NATIVE_DRIVER` | `databases[].optimizationSettings.nativeDriver` |
| `DB_QUESTDB_SYNC_WAL` | `databases[].optimizationSettings.syncWal` |
| `DB_ORACLE_CAMEL_CASE` | `databases[].optimizationSettings.camelCase` |
| `DB_POSTGRES_AUTO_TO_LOWER` | `databases[].optimizationSettings.autoToLower` |
| `DB_SQLITE_ENABLE_DEFAULT_VALUE` | `databases[].optimizationSettings.enableDefaultValue` |
| `DB_DISABLE_NVARCHAR` | `databases[].optimizationSettings.disableNvarchar` |

完整映射表请参考各数据库配置文档。

#### 3. 自动迁移检测

如果您仍在使用旧的环境变量配置，DatabaseMcpServer 2.0.1-test 会自动检测并显示详细的迁移提示。

### 常用数据库连接字符串示例

| 数据库 | 连接字符串示例 | 详细文档 |
|--------|---------------|---------|
| **MySQL** | `Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;` | [MySQL.md](DatabaseSetting/MySQL.md) |
| **SQL Server** | `Server=localhost;Database=mydb;User Id=sa;Password=123456;` | [SQLServer.md](DatabaseSetting/SQLServer.md) |
| **PostgreSQL** | `Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;` | [PostgreSQL.md](DatabaseSetting/PostgreSQL.md) |
| **Oracle** | `Data Source=localhost/orcl;User ID=system;Password=oracle123;` | [Oracle.md](DatabaseSetting/Oracle.md) |
| **SQLite** | `Data Source=mydb.db;` | [SQLite.md](DatabaseSetting/SQLite.md) |
| **达梦数据库** | `Server=localhost;Port=5236;Database=mydb;User=SYSDBA;Password=SYSDBA001;` | [DM.md](DatabaseSetting/DM.md) |
| **人大金仓** | `Server=localhost;Port=54321;Database=mydb;User=SYSTEM;Password=system123;` | [Kdbndp.md](DatabaseSetting/Kdbndp.md) |
| **GaussDB** | `PORT=5432;DATABASE=mydb;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;` | [GaussDB.md](DatabaseSetting/GaussDB.md) |
| **QuestDB** | `host=localhost;port=8812;username=admin;password=quest;database=mydb;` | [QuestDB.md](DatabaseSetting/QuestDB.md) |

更多连接字符串和优化配置请参考 [DatabaseSetting/](DatabaseSetting/) 目录下的详细文档。

---

## 📋 完整功能清单（55+ 工具）

### 🔌 一、连接与配置管理（9 个工具）

**基础连接管理**:
- **test_connection** - 测试当前数据库连接
- **test_connection_by_name** - 测试指定数据库的连接
- **get_database_config** - 获取当前数据库配置信息
- **validate_configuration** - 验证数据库配置是否正确

**多数据库管理**:
- **list_databases** - 列出所有可用的数据库连接
- **switch_database** - 切换到指定的数据库
- **get_current_database** - 获取当前活动的数据库

**性能与健康检查**:
- **health_check** - 对所有数据库连接执行健康检查（响应时间、连接状态）
- **test_connection_with_retry** - 带自动重试的连接测试（指数退避策略）

### 🔍 二、数据库架构查询（12 个工具）

- **get_data_base_list** - 获取所有数据库名称
- **get_table_info_list** - 获取所有表名
- **get_view_info_list** - 查询所有视图
- **get_column_infos_by_table_name** - 根据表名获取字段信息
- **get_table_schema** - 获取表的完整结构信息
- **get_is_identities** - 获取自增列
- **get_primaries** - 获取主键
- **get_index_list** - 获取所有索引名字集合
- **get_proc_list** - 获取存储过程名字集合
- **get_func_list** - 获取函数集合
- **get_trigger_names** - 根据表名获取触发器集合
- **get_db_types** - 获取数据库类型集合

### 🔎 三、存在性检查（7 个工具）

- **is_any_table** - 判断表是否存在
- **is_any_column** - 判断列是否存在
- **is_primary_key** - 判断主键是否存在
- **is_identity** - 判断自增是否存在
- **is_any_constraint** - 判断约束是否存在
- **is_any_index** - 判断索引是否存在
- **is_any_table_remark** - 判断是否存在表描述

### 📊 四、数据查询工具（17 个工具）

**基础查询：**
- **sql_query** - 执行 SQL 查询并返回强类型实体集合（支持参数化查询）
- **sql_query_single** - 执行 SQL 查询并返回单条记录
- **get_data_reader** - 获取 DataReader 数据（自动处理释放）

**高级查询：**
- **get_data_set_all** - 获取多个结果集，支持一次执行多个查询
- **sql_query_multiple** - 执行查询并返回两个结果集
- **sql_query_with_in_parameter** - 处理 IN 参数查询，支持数组参数

**标量值查询：**
- **get_scalar** - 获取首行首列的值（标量值）
- **get_string** - 获取首行首列的字符串值
- **get_int** - 获取首行首列的整数值
- **get_long** - 获取首行首列的长整数值
- **get_double** - 获取首行首列的双精度浮点数值
- **get_decimal** - 获取首行首列的十进制数值
- **get_date_time** - 获取首行首列的日期时间值

### ✏️ 五、数据操作工具（9 个工具）

- **execute_command** - 执行 SQL 命令（INSERT、UPDATE、DELETE）
- **insert_data** - 向表中插入数据
- **update_data** - 更新表中的数据
- **delete_data** - 从表中删除数据
- **execute_transaction** - 执行包含多条 SQL 命令的事务
- **batch_execute_commands** - 批量执行 SQL 命令（性能优化）
- **call_stored_procedure** - 调用存储过程（简单用法）
- **call_stored_procedure_with_output** - 调用带有输出参数的存储过程
- **execute_command_with_go** - 执行包含 GO 语句的 SQL Server 脚本

### 🛠️ 六、数据库架构操作（高风险）（6 个核心工具）

**表操作：**
- **drop_table** - 删除表
- **truncate_table** - 清空表
- **backup_table** - 备份表
- **rename_table** - 重命名表

**列操作：**
- **add_column** - 添加列
- **update_column** - 更新列
- **drop_column** - 删除列
- **rename_column** - 重命名列

**约束和索引：**
- **add_primary_key** - 添加主键
- **drop_constraint** - 删除约束
- **create_index** - 创建索引或唯一约束

**其他：**
- **add_default_value** - 添加默认值
- **add_table_remark** - 添加表描述
- **add_column_remark** - 添加列描述

*完整工具列表请参考 [.mcp/server.json](.mcp/server.json)*

## 💡 使用示例

### 示例 1：基础连接与查询

**测试数据库连接**
```
测试数据库连接
```

**列出所有表**
```
列出当前数据库的所有表
```

**查询用户数据**
```
查询 users 表中的所有数据
```

### 示例 2：参数化查询

**条件查询**
```
查询 users 表中年龄大于 25 岁的活跃用户，按创建时间倒序排列
```

**IN 参数查询**
```
查询用户ID在 [1,2,3,4,5] 中的用户信息
```

**多条件查询**
```
查询城市为"北京"、年龄在 20-30 之间、状态为活跃的用户
```

### 示例 3：数据统计与分析

**聚合查询**
```
统计 products 表中每个分类的商品数量和平均价格
```

**多结果集查询**
```
同时查询：1) 用户总数和活跃用户数量 2) 最近 7 天的订单数据
```

**标量值查询**
```
获取订单表中订单状态为"已完成"的总金额
```

### 示例 4：数据操作

**插入新数据**
```
向 products 表插入新商品：名称为"MacBook Pro M3"，价格为 14999，库存为 50
```

**批量更新**
```
批量更新以下用户的VIP状态：用户ID 1,3,5,7,9 设置为VIP，其他设置为普通用户
```

**事务操作**
```
执行转账操作：从账户A(ID:1001)转账 500 元到账户B(ID:1002)
```

### 示例 5：架构查询

**获取表结构**
```
获取 orders 表的完整结构信息：列、主键、索引、自增列等
```

**查询索引信息**
```
查询 users 表的所有索引信息
```

**检查表是否存在**
```
检查数据库中是否存在名为"user_logs"的表
```

### 示例 6：存储过程调用

**简单存储过程**
```
调用存储过程 sp_monthly_report，传入参数年份 2025，月份 11
```

**带输出参数的存储过程**
```
调用存储过程 sp_user_statistics，传入用户ID 1001，获取该用户的订单总数和总金额
```

## 🔒 安全特性

### 危险操作检测
系统自动检测并阻止以下危险操作：
- `DROP TABLE` / `DROP DATABASE` - 删除表/数据库
- `TRUNCATE TABLE` - 清空表数据
- `ALTER TABLE` - 修改表结构
- 无 WHERE 条件的 `DELETE` / `UPDATE`

如需执行这些操作，请使用专门的架构操作工具（如 `drop_table`、`truncate_table` 等），这些工具会明确提示风险。

### SQL 注入防护
所有查询都支持参数化查询，自动防止 SQL 注入：

```json
{
  "sql": "SELECT * FROM users WHERE age > @age AND city = @city",
  "parameters": "{\"age\":18,\"city\":\"北京\"}"
}
```

### 敏感信息保护
- 连接字符串中的密码自动隐藏（显示为 `Password=****`）
- 日志中不输出完整连接字符串
- 配置信息返回时自动脱敏

## 💻 开发指南

### 本地开发

```bash
# 克隆项目
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer

# 创建配置文件 databases.json 后运行
DB_CONFIG_PATH="path/to/databases.json" dotnet run

# 构建项目
dotnet build

# 运行测试
dotnet test

# 打包发布
dotnet pack -c Release
```

### 添加新工具

1. **创建工具类文件**
   ```bash
   # 在 Tools/ 目录下创建新工具类
   # Management/ - 连接和架构管理
   # Query/ - 查询工具
   # Command/ - 命令工具
   ```

2. **实现工具类**
   ```csharp
   using System.ComponentModel;
   using ModelContextProtocol.Server;
   using DatabaseMcpServer.Interfaces;

   namespace DatabaseMcpServer.Tools;

   internal class YourNewTools
   {
       private readonly IDatabaseConfigService _databaseConfig;
       private readonly IDatabaseHelperService _databaseHelper;

       public YourNewTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper)
       {
           _databaseConfig = databaseConfig;
           _databaseHelper = databaseHelper;
       }

       [McpServerTool]
       [Description("你的工具描述")]
       public string YourMethod([Description("参数描述")] string parameter)
       {
           using var db = _databaseConfig.CreateClient();
           // 实现你的功能
           return _databaseHelper.SerializeResult(new { success = true, data = "result" });
       }
   }
   ```

3. **注册工具**
   在 `Program.cs` 中：
   ```csharp
   builder.Services
       .AddMcpServer()
       .WithStdioServerTransport()
       .WithTools<ConnectionTools>()
       .WithTools<SchemaTools>()
       .WithTools<QueryTools>()
       .WithTools<CommandTools>()
       .WithTools<YourNewTools>(); // 添加你的工具
   ```

### 项目架构

```
MCP Protocol Layer (stdio)
    ↓
Tools Layer (Connection/Query/Command/Schema)
    ↓
Services Layer (DatabaseConfigService)
    ↓
Data Access Layer (SqlSugar ORM)
```

**关键组件：**
- `DatabaseConfigService` - 配置管理和连接创建
- `DatabaseHelper` - 数据库类型解析和安全检查
- `McpExceptionFilter` - 统一异常处理
- `ApiResult<T>` - 标准化返回格式

## 🛠️ 技术栈

- **.NET 9.0** - 最新的 .NET 平台
- **ModelContextProtocol 0.4.0** - MCP 协议 C# SDK
- **SqlSugarCore 5.1.4** - 轻量级高性能 ORM
- **Serilog** - 结构化日志框架
- **Microsoft.Extensions.Hosting** - 依赖注入和托管

## 📚 相关资源

- [MCP 官方文档](https://modelcontextprotocol.io/)
- [MCP GitHub](https://github.com/modelcontextprotocol)
- [SqlSugar 文档](https://github.com/DotNetNext/SqlSugar)
- [VS Code MCP 指南](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

1. Fork 项目
2. 创建特性分支：`git checkout -b feature/AmazingFeature`
3. 提交更改：`git commit -m 'Add AmazingFeature'`
4. 推送到分支：`git push origin feature/AmazingFeature`
5. 开启 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件。

## ⚠️ 免责声明

- 本项目已发布 2.0.1-test 正式版本
- 2.0.1-test 版本包含破坏性变更，请参考迁移指南
- 生产环境使用前请充分测试
- 定期备份重要数据
- 注意配置中的敏感信息保护

---

**DatabaseMCP** - 让 AI 助手轻松操作数据库！
