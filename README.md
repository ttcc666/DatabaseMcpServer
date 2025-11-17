# DatabaseMCP 数据库操作服务器

[![NuGet](https://img.shields.io/nuget/v/DatabaseMcpServer.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![.NET Tool](https://img.shields.io/badge/.NET%20Tool-1.0.5-blue.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[🇺🇸 English](README_EN.md) | [🇨🇳 中文](README.md)

一个功能强大的数据库操作 MCP (Model Context Protocol) 服务器，支持 **34 种数据库类型**，通过环境变量配置连接信息，让 AI 助手能够安全、便捷地执行数据库操作。

## ✨ 核心特性

- 🗄️ **多数据库支持** - 支持 34 种数据库类型（主流、国产、分布式、时序）
- 🔒 **安全防护** - 危险操作检测 + SQL 注入防护 + 敏感信息保护
- ⚡ **高性能** - 基于 SqlSugar ORM，提供高效的数据库访问
- 🔧 **环境变量配置** - 全局配置，无需每次传参
- 💾 **完整功能** - 47 个 MCP 工具，涵盖查询、操作、架构管理等
- 🚀 **生产就绪** - 支持事务、批量操作、存储过程
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

### 第二步：配置 MCP 客户端

创建 `mcp.json` 配置文件（VS Code: `.vscode/mcp.json`）:

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

### 第三步：测试连接并执行查询

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
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

### 方式 2：dnx 命令

**安装**：
```bash
dnx DatabaseMcpServer@1.0.5 --yes
```

**MCP 配置**：
```json
{
  "mcpServers": {
    "database": {
      "command": "dnx",
      "args": ["DatabaseMcpServer@1.0.5", "--yes"],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
        "DB_TYPE": "MySql"
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
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

## ⚙️ 配置指南

### 必需环境变量

| 变量名 | 说明 | 示例 |
|--------|------|------|
| `DB_CONNECTION_STRING` | 数据库连接字符串（必需） | `Server=localhost;Database=mydb;User=root;Password=123456;` |

### 可选环境变量

| 变量名 | 说明 | 默认值 | 示例 |
|--------|------|--------|------|
| `DB_TYPE` | 数据库类型 | `MySql` | `SqlServer`、`PostgreSQL`、`Oracle` 等 |
| `SEQ_SERVER_URL` | Seq 日志服务器地址 | - | `http://localhost:5341` |
| `SEQ_API_KEY` | Seq API 密钥 | - | `your-seq-api-key` |
| `DB_DDL_WHITELIST` | 允许跳过危险 SQL 检测的 DDL 正则白名单（分号分隔） | - | `(?i)^CREATE\\s+TABLE\\s+temp_.*$;ALTER\\s+TABLE\\s+staging\\.` |

### 常用数据库连接字符串示例

**MySQL**
```
Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;
```

**SQL Server**
```
Server=localhost;Database=mydb;User Id=sa;Password=123456;
```

**SQLite**
```
Data Source=mydb.db;
```

**PostgreSQL**
```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;
```

**达梦数据库**
```
Server=localhost;Port=5236;Database=mydb;User=SYSDBA;Password=SYSDBA001;
```

**OceanBase**
```
Server=localhost;Port=2881;Database=mydb;User=root@sys;Password=123456;
```

更多连接字符串请参考 [mcp.json.example](mcp.json.example) 文件。

## 📋 完整功能清单（47 个工具）

### 🔌 一、连接与配置管理（3 个工具）

- **test_connection** - 测试数据库连接
- **get_database_config** - 获取当前数据库配置信息
- **validate_configuration** - 验证数据库配置是否正确

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

# 设置环境变量后运行
DB_CONNECTION_STRING="your_connection" DB_TYPE="MySql" dotnet run

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

- 本项目已发布 1.0.5 正式版本
- 生产环境使用前请充分测试
- 定期备份重要数据
- 注意配置中的敏感信息保护

---

**DatabaseMCP** - 让 AI 助手轻松操作数据库！
