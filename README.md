# 数据库操作 MCP 服务器

[![NuGet](https://img.shields.io/nuget/v/DatabaseMcpServer.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

一个功能强大的数据库操作 MCP (Model Context Protocol) 服务器，支持多种主流数据库。通过环境变量配置连接信息，让 AI 助手能够安全、便捷地执行数据库操作。

## ✨ 特性

- 🗄️ **多数据库支持**：MySQL、SQL Server、SQLite、PostgreSQL、Oracle
- 🔒 **安全防护**：内置危险操作检测，防止意外的破坏性操作
- 🚀 **高性能**：基于 SqlSugar ORM，提供高效的数据库访问
- 📦 **自包含部署**：无需在目标机器上安装 .NET 运行时
- 🌍 **跨平台支持**：支持 Windows、macOS、Linux 多种架构
- 🔧 **环境变量配置**：通过环境变量全局配置，无需每次传参
- 💾 **事务支持**：支持多条 SQL 命令的事务操作
- 🛡️ **参数化查询**：防止 SQL 注入攻击

## 📋 功能清单

### 🔌 连接与配置管理

- **test_connection** - 测试数据库连接
- **get_database_config** - 获取当前数据库配置信息
- **validate_configuration** - 验证数据库配置是否正确

### 🔍 数据库架构查询

- **get_data_base_list** - 获取所有数据库名称
- **get_table_info_list** - 获取所有表名
- **get_view_info_list** - 查询所有视图
- **get_column_infos_by_table_name** - 根据表名获取字段信息
- **get_table_schema** - 获取表的完整结构信息（列、主键、索引、自增列）
- **get_is_identities** - 获取自增列
- **get_primaries** - 获取主键
- **get_index_list** - 获取所有索引名字集合
- **get_proc_list** - 获取存储过程名字集合
- **get_func_list** - 获取函数集合
- **get_trigger_names** - 根据表名获取触发器集合
- **get_db_types** - 获取数据库类型集合

### 🔎 存在性检查

- **is_any_table** - 判断表是否存在
- **is_any_column** - 判断列是否存在
- **is_primary_key** - 判断主键是否存在
- **is_identity** - 判断自增是否存在
- **is_any_constraint** - 判断约束是否存在
- **is_any_index** - 判断索引是否存在
- **is_any_table_remark** - 判断是否存在表描述

### 📊 数据查询工具

#### 基础查询
- **sql_query** - 执行 SQL 查询并返回强类型实体集合（支持参数化查询）
- **sql_query_single** - 执行 SQL 查询并返回单条记录
- **get_data_reader** - 获取 DataReader 数据（自动处理释放）

#### 高级查询
- **get_data_set_all** - 获取多个结果集，支持一次执行多个查询
- **sql_query_multiple** - 执行查询并返回两个结果集
- **sql_query_with_in_parameter** - 处理 IN 参数查询，支持数组参数

#### 标量值查询
- **get_scalar** - 获取首行首列的值（标量值）
- **get_string** - 获取首行首列的字符串值
- **get_int** - 获取首行首列的整数值
- **get_long** - 获取首行首列的长整数值
- **get_double** - 获取首行首列的双精度浮点数值
- **get_decimal** - 获取首行首列的十进制数值
- **get_date_time** - 获取首行首列的日期时间值

### ✏️ 数据操作工具

- **execute_command** - 执行 SQL 命令（INSERT、UPDATE、DELETE）
- **insert_data** - 向表中插入数据
- **update_data** - 更新表中的数据
- **delete_data** - 从表中删除数据

### 🔄 事务与批量操作

- **execute_transaction** - 执行包含多条 SQL 命令的事务
- **batch_execute_commands** - 批量执行 SQL 命令（使用长连接优化性能）

### 📞 存储过程调用

- **call_stored_procedure** - 调用存储过程（简单用法）
- **call_stored_procedure_with_output** - 调用带有输出参数的存储过程

### 🛠️ 数据库架构操作（高风险）

#### 表操作
- **drop_table** - 删除表
- **truncate_table** - 清空表
- **backup_table** - 备份表
- **rename_table** - 重命名表

#### 列操作
- **add_column** - 添加列
- **update_column** - 更新列
- **drop_column** - 删除列
- **rename_column** - 重命名列

#### 约束和索引操作
- **add_primary_key** - 添加主键
- **drop_constraint** - 删除约束
- **create_index** - 创建索引或唯一约束

#### 默认值和注释
- **add_default_value** - 添加默认值
- **add_table_remark** - 添加表描述
- **delete_table_remark** - 删除表描述
- **add_column_remark** - 添加列描述
- **delete_column_remark** - 删除列描述

#### 存储过程、函数、视图操作
- **drop_view** - 删除视图
- **drop_func** - 删除函数
- **drop_proc** - 删除存储过程

### 🔧 SQL Server 特殊支持

- **execute_command_with_go** - 执行包含 GO 语句的 SQL Server 脚本

## 🚀 快速开始

### 本地开发测试

1. **克隆项目**
```bash
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer
```

2. **配置 MCP 客户端**

复制 `mcp.json.example` 到你的 IDE 配置目录并修改连接信息：

**VS Code**: 复制到 `<WORKSPACE>/.vscode/mcp.json`
**Visual Studio**: 复制到 `<SOLUTION>/.mcp.json`

详细配置说明请参考 [配置指南](#-配置指南)

3. **测试服务器**

在 Copilot Chat 中尝试以下命令（无需提供连接信息）：
- "测试数据库连接"
- "列出当前数据库的所有表"
- "查询 users 表的所有数据"
- "获取 products 表的结构信息"

## ⚙️ 配置指南

### 环境变量说明

| 变量名 | 说明 | 必需 | 默认值 | 示例 |
|--------|------|------|--------|------|
| `DB_CONNECTION_STRING` | 数据库连接字符串 | ✅ 是 | 无 | `Server=localhost;Database=mydb;User=root;Password=123456;` |
| `DB_TYPE` | 数据库类型 | ❌ 否 | `MySql` | `MySql`, `SqlServer`, `Sqlite`, `PostgreSQL`, `Oracle` |
| `SEQ_SERVER_URL` | Seq 日志服务器地址 | ❌ 否 | 无 | `http://localhost:5341` |
| `SEQ_API_KEY` | Seq API 密钥 | ❌ 否 | 无 | `your-seq-api-key` |

### 连接字符串示例

#### MySQL
```
Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;
```

#### SQL Server
```
Server=localhost;Database=mydb;User Id=sa;Password=123456;
```

#### SQLite
```
Data Source=mydb.db;
```

#### PostgreSQL
```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;
```

#### Oracle
```
Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)));User Id=system;Password=123456;
```

### MCP 配置文件

#### 基础配置
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer.exe",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=password;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

#### 启用 Seq 日志记录
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer.exe",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=password;",
        "DB_TYPE": "MySql",
        "SEQ_SERVER_URL": "http://localhost:5341",
        "SEQ_API_KEY": "your-seq-api-key"
      }
    }
  }
}
```

**本地开发**: 修改 `mcp.json.example` 中的连接信息后复制到对应位置
**NuGet 包**: 将 `command` 改为 `"dnx"` 并设置 `args` 为 `["DatabaseMcpServer", "--version", "1.0.2", "--yes"]`

## 📦 从 NuGet 安装

使用 NuGet 包时，只需修改配置文件中的 `command` 和 `args` 字段，环境变量配置保持不变。

## 💻 使用示例

### 示例 1：连接管理

**测试数据库连接**
```
测试数据库连接
```

返回：
```json
{
  "success": true,
  "data": {
    "connected": true,
    "databaseType": "MySql",
    "serverVersion": "8.0.35"
  },
  "message": "数据库连接成功"
}
```

**验证配置**
```
验证当前数据库配置
```

返回：
```json
{
  "success": true,
  "data": {
    "configured": true,
    "databaseType": "MySql",
    "connectionString": "Server=localhost;Database=mydb;User=root;Password=****;",
    "isValid": true
  },
  "message": "配置验证通过"
}
```

### 示例 2：架构查询

**获取所有表**
```
列出当前数据库的所有表
```

**获取表结构**
```
获取 users 表的完整结构信息
```

返回：
```json
{
  "success": true,
  "data": {
    "tableName": "users",
    "columns": [
      {
        "columnName": "id",
        "dataType": "int",
        "isNullable": false,
        "isPrimaryKey": true,
        "isIdentity": true
      },
      {
        "columnName": "username",
        "dataType": "varchar(50)",
        "isNullable": false,
        "isPrimaryKey": false,
        "isIdentity": false
      }
    ],
    "indexes": ["PRIMARY", "idx_username"],
    "primaryKeys": ["id"]
  }
}
```

### 示例 3：数据查询

**基础查询**
```
查询 users 表中年龄大于 25 岁的活跃用户
```

**参数化查询**
```
查询指定城市和年龄范围的用户：城市为"北京"，年龄在 20-30 之间
```

**聚合查询**
```
统计每个部门的员工数量和平均薪资
```

**IN 参数查询**
```
查询用户ID在 [1, 5, 10, 15, 20] 中的用户详细信息
```

### 示例 4：数据操作

**插入数据**
```
向 products 表插入新商品：
{
  "name": "MacBook Pro M3",
  "price": 14999,
  "category": "电脑",
  "stock": 50,
  "description": "Apple MacBook Pro 14英寸 M3芯片"
}
```

**更新数据**
```
将商品ID为5的库存更新为100，价格更新为8999
```

**删除数据**
```
删除状态为"已停用"且创建时间超过1年的用户记录
```

### 示例 5：高级查询

**多结果集查询**
```
同时获取用户统计信息和最近订单数据
```

AI 会执行：
```sql
-- 第一个结果集：用户统计
SELECT
  COUNT(*) as total_users,
  COUNT(CASE WHEN status = 'active' THEN 1 END) as active_users,
  AVG(age) as avg_age
FROM users;

-- 第二个结果集：最近订单
SELECT
  o.id, o.user_id, u.username, o.amount, o.created_at
FROM orders o
JOIN users u ON o.user_id = u.id
WHERE o.created_at >= DATE_SUB(NOW(), INTERVAL 7 DAY)
ORDER BY o.created_at DESC
LIMIT 10;
```

**复杂关联查询**
```
查询最近30天内有订单的用户信息，包括订单总数和总金额
```

### 示例 6：事务操作

**转账事务**
```
执行转账操作：从账户A(ID:1001)转账500元到账户B(ID:1002)
```

AI 会执行事务：
```sql
BEGIN TRANSACTION;
UPDATE accounts SET balance = balance - 500 WHERE id = 1001 AND balance >= 500;
UPDATE accounts SET balance = balance + 500 WHERE id = 1002;
COMMIT;
```

**批量操作**
```
批量更新以下用户的VIP状态：
- 用户ID 1,3,5,7,9 设置为VIP
- 用户ID 2,4,6,8,10 设置为普通用户
```

### 示例 7：存储过程调用

**简单存储过程**
```
调用存储过程 sp_monthly_report，传入参数：年份2024，月份11
```

**带输出参数的存储过程**
```
调用存储过程 sp_user_statistics，传入用户ID 1001，获取输出参数：total_orders, total_amount, last_order_date
```

返回：
```json
{
  "success": true,
  "data": {
    "inputParameters": {
      "user_id": 1001
    },
    "outputParameters": {
      "total_orders": 25,
      "total_amount": 15680.50,
      "last_order_date": "2024-11-05"
    }
  }
}
```

### 示例 8：架构管理

**表操作**
```
创建用户日志表的备份表 user_logs_backup
```

**索引管理**
```
为 orders 表的 user_id 字段创建索引 idx_orders_user_id
```

**列操作**
```
为 users 表添加新列：phone varchar(20)，允许为空，默认值为空字符串
```

### 示例 9：数据分析

**销售报表**
```
生成本月销售报表：按产品分类统计销量和销售额
```

**用户行为分析**
```
分析用户登录频率：统计每个用户最近30天的登录次数
```

**库存预警**
```
查询库存不足的商品（库存小于10件）
```

### 示例 10：实用工具

**数据导出**
```
导出所有活跃用户的基本信息到JSON格式
```

**数据验证**
```
检查 orders 表中是否存在无效的用户ID引用
```

**性能分析**
```
分析 users 表的索引使用情况和查询性能
```

## 🔒 安全特性

### 危险操作检测

服务器会自动检测并阻止以下危险操作：
- `DROP TABLE` - 删除表
- `DROP DATABASE` - 删除数据库
- `TRUNCATE TABLE` - 截断表
- `ALTER TABLE` - 修改表结构
- `CREATE TABLE` - 创建表

如需执行这些操作，请使用专门的架构操作工具（如 `drop_table`, `truncate_table` 等）。

### SQL 注入防护

所有查询和命令都支持参数化查询，示例：

```
查询年龄大于 18 且城市为北京的用户
```

AI 会自动生成参数化查询：
```json
{
  "sql": "SELECT * FROM users WHERE age > @age AND city = @city",
  "parameters": "{\"age\":18,\"city\":\"Beijing\"}"
}
```

### 敏感信息保护

- 配置信息中的密码会自动隐藏
- 连接字符串在日志中显示为 `Password=****`

## 🔧 开发指南

### 构建项目

```bash
# 恢复依赖
dotnet restore

# 构建项目
dotnet build

# 运行项目
dotnet run

# 打包发布
dotnet pack -c Release
```

### 支持的平台

默认支持以下平台：
- `win-x64` - Windows 64位

如需添加更多平台，请在 `.csproj` 文件中修改 `<RuntimeIdentifiers>` 元素。

### 添加新工具

1. 在 `Tools` 目录下创建新的工具类
2. 使用 `[McpServerTool]` 特性标记方法
3. 使用 `[Description]` 特性添加中文描述
4. 通过 `DatabaseConfigService` 获取数据库连接
5. 在 `Program.cs` 中注册工具：

```csharp
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ConnectionTools>()
    .WithTools<SchemaTools>()
    .WithTools<QueryTools>()
    .WithTools<CommandTools>()
    .WithTools<YourNewTools>();  // 添加你的新工具
```

### 工具开发示例

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

internal class YourNewTools
{
    [McpServerTool]
    [Description("你的工具描述")]
    public string YourMethod(
        [Description("参数描述")] string parameter)
    {
        // 使用全局配置创建数据库客户端
        using var db = DatabaseConfigService.CreateGlobalClient();

        // 执行数据库操作
        var result = db.Queryable<YourEntity>().ToList();

        // 返回 JSON 结果
        return DatabaseHelper.SerializeResult(new { success = true, data = result });
    }
}
```


## 🛠️ 技术栈

- **.NET 9.0** - 最新的 .NET 框架
- **ModelContextProtocol** - MCP 协议 C# SDK
- **SqlSugar** - 轻量级高性能 ORM
- **Microsoft.Extensions.Hosting** - 依赖注入和托管服务

## 📝 发布到 NuGet

### 发布前检查清单

- [ ] 本地测试所有功能
- [ ] 更新 `.csproj` 中的包元数据
  - `<PackageId>`
  - `<PackageVersion>`
  - `<Description>`
  - `<Authors>`
- [ ] 更新 `.mcp/server.json`
- [ ] 更新 README.md
- [ ] 添加许可证文件

### 发布步骤

1. **打包项目**
```bash
dotnet pack -c Release
```

2. **发布到 NuGet.org**
```bash
dotnet nuget push bin/Release/*.nupkg --api-key <your-api-key> --source https://api.nuget.org/v3/index.json
```

3. **验证发布**
访问 [NuGet.org](https://www.nuget.org/packages/DatabaseMcpServer) 确认包已成功发布。

## 📖 相关资源

### MCP 相关
- [MCP 官方文档](https://modelcontextprotocol.io/)
- [MCP 协议规范](https://spec.modelcontextprotocol.io/)
- [MCP GitHub 组织](https://github.com/modelcontextprotocol)
- [ModelContextProtocol NuGet 包](https://www.nuget.org/packages/ModelContextProtocol)

### IDE 集成
- [VS Code 中使用 MCP 服务器](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
- [Visual Studio 中使用 MCP 服务器](https://learn.microsoft.com/visualstudio/ide/mcp-servers)

### .NET MCP 开发
- [.NET MCP 服务器开发指南](https://aka.ms/nuget/mcp/guide)
- [配置输入参数](https://aka.ms/nuget/mcp/guide/configuring-inputs)

## 🎯 核心优势

### 为什么使用环境变量配置？

1. **简化调用** - 所有工具方法无需传入连接参数
2. **集中管理** - 在一个地方配置，全局使用
3. **安全性高** - 敏感信息不会在工具调用中暴露
4. **易于切换** - 修改配置文件即可切换数据库环境
5. **符合最佳实践** - 遵循 12-Factor App 配置原则

### 对比传统方式

**传统方式（每次都要传参）：**
```
查询 users 表，使用连接字符串：Server=localhost;Database=mydb;User=root;Password=123456;，数据库类型：MySql
```

**现在的方式（无需传参）：**
```
查询 users 表
```

## 🤝 贡献

欢迎贡献代码、报告问题或提出新功能建议！

1. Fork 本项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件。

## 💬 反馈

如果你对这个项目有任何反馈，请参与 [简短调查](http://aka.ms/dotnet-mcp-template-survey)。

## ⚠️ 免责声明

- 本项目已发布 1.0.2 正式版本
- 请在生产环境中谨慎使用
- 始终备份重要数据
- 确保正确配置安全设置
- 不要在公共仓库中提交包含真实密码的配置文件

## 🙏 致谢

- [Anthropic](https://www.anthropic.com/) - MCP 协议的创建者
- [SqlSugar](https://github.com/DotNetNext/SqlSugar) - 优秀的 ORM 框架
- [.NET 团队](https://dotnet.microsoft.com/) - 强大的开发平台

---

**注意**：
1. 请将文档中的占位符（如 GitHub 用户名、包 ID 等）替换为实际值后再发布
2. 配置文件中的数据库密码仅用于示例，请使用你自己的安全凭据
3. 建议使用 `.gitignore` 忽略包含敏感信息的配置文件
