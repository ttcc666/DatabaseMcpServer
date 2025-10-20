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

### 🔌 连接管理工具

- **test_connection** - 测试数据库连接
- **list_databases** - 列出所有数据库
- **list_tables** - 列出当前数据库中的所有表
- **get_table_schema** - 获取表的详细结构信息（只需提供表名）

### 🔍 查询工具

- **execute_query** - 执行 SQL 查询并返回结果（支持参数化查询）
- **simple_query** - 执行不带参数的简单查询

### ✏️ 数据操作工具

- **execute_command** - 执行 SQL 命令（INSERT、UPDATE、DELETE）
- **insert_data** - 向表中插入数据（只需提供表名和数据）
- **update_data** - 更新表中的数据（只需提供表名、数据和条件）
- **delete_data** - 从表中删除数据（只需提供表名和条件）
- **execute_transaction** - 执行包含多条 SQL 命令的事务

### ⚙️ 配置工具

- **get_database_config** - 获取当前数据库配置信息
- **validate_configuration** - 验证数据库配置是否正确

### 🎲 示例工具

- **get_random_number** - 生成随机数（用于演示）

## 🚀 快速开始

### 本地开发测试

1. **克隆项目**

```bash
git clone https://github.com/yourusername/database-mcp-server.git
cd database-mcp-server
```

2. **配置 MCP 客户端**

在你的 IDE 中创建 MCP 配置文件，并通过 `env` 字段设置数据库连接信息：

**VS Code** (`<WORKSPACE>/.vscode/mcp.json`):
```json
{
  "mcpServers": {
    "DatabaseMcpServer": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:/Demo/my-mcp/SampleMcpServer"
      ],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

**Visual Studio** (`<SOLUTION>/.mcp.json`):
```json
{
  "mcpServers": {
    "DatabaseMcpServer": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\Demo\\my-mcp\\SampleMcpServer"
      ],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

3. **环境变量说明**

| 变量名 | 说明 | 必需 | 默认值 | 示例 |
|--------|------|------|--------|------|
| `DB_CONNECTION_STRING` | 数据库连接字符串 | ✅ 是 | 无 | `Server=localhost;Database=mydb;User=root;Password=123456;` |
| `DB_TYPE` | 数据库类型 | ❌ 否 | `MySql` | `MySql`, `SqlServer`, `Sqlite`, `PostgreSQL`, `Oracle` |

4. **测试服务器**

在 Copilot Chat 中尝试以下命令（无需提供连接信息）：
- "测试数据库连接"
- "列出当前数据库的所有表"
- "查询 users 表的所有数据"
- "获取 products 表的结构信息"

## 📦 从 NuGet 安装

1. **在 IDE 中配置**

**VS Code** (`<WORKSPACE>/.vscode/mcp.json`):
```json
{
  "mcpServers": {
    "DatabaseMcpServer": {
      "command": "dnx",
      "args": [
        "DatabaseMcpServer",
        "--version",
        "0.1.0-beta",
        "--yes"
      ],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

**Visual Studio** (`<SOLUTION>/.mcp.json`):
```json
{
  "mcpServers": {
    "DatabaseMcpServer": {
      "command": "dnx",
      "args": [
        "DatabaseMcpServer",
        "--version",
        "0.1.0-beta",
        "--yes"
      ],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

## 💻 使用示例

### 示例 1：测试连接

```
测试数据库连接
```

返回：
```json
{
  "success": true,
  "message": "连接成功",
  "connected": true,
  "databaseType": "MySql"
}
```

### 示例 2：查询数据

```
查询 users 表中年龄大于 18 岁的用户
```

AI 会自动使用环境变量中的连接信息执行查询。

### 示例 3：插入数据

```
向 products 表插入一条新记录：
{
  "name": "iPhone 15",
  "price": 5999,
  "stock": 100
}
```

### 示例 4：获取表结构

```
获取 users 表的结构信息
```

### 示例 5：事务操作

```
执行以下事务操作：
1. 从账户 A 扣除 100 元
2. 向账户 B 增加 100 元
```

### 示例 6：验证配置

```
验证数据库配置是否正确
```

返回：
```json
{
  "configured": true,
  "databaseType": "MySql",
  "connectionString": "Server=localhost;Database=mydb;User=root;Password=****;",
  "message": "配置有效"
}
```

## 🔒 安全特性

### 危险操作检测

服务器会自动检测并阻止以下危险操作：
- `DROP TABLE` - 删除表
- `DROP DATABASE` - 删除数据库
- `TRUNCATE TABLE` - 截断表
- `ALTER TABLE` - 修改表结构
- `CREATE TABLE` - 创建表

如需执行这些操作，请使用专门的数据库管理工具。

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
- `win-arm64` - Windows ARM64
- `osx-arm64` - macOS ARM64 (Apple Silicon)
- `linux-x64` - Linux 64位
- `linux-arm64` - Linux ARM64
- `linux-musl-x64` - Alpine Linux 64位

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
    .WithTools<RandomNumberTools>()
    .WithTools<DatabaseConnectionTools>()
    .WithTools<DatabaseQueryTools>()
    .WithTools<DatabaseCommandTools>()
    .WithTools<ConfigurationTools>()
    .WithTools<YourNewTools>();  // 添加你的新工具
```

### 工具开发示例

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using SampleMcpServer.Services;

namespace SampleMcpServer.Tools;

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

## 📚 数据库连接字符串示例

### MySQL
```
Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;
```

### SQL Server
```
Server=localhost;Database=mydb;User Id=sa;Password=123456;
```

### SQLite
```
Data Source=mydb.db;
```

### PostgreSQL
```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;
```

### Oracle
```
Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)));User Id=system;Password=123456;
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

- 本项目目前处于早期预览阶段
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