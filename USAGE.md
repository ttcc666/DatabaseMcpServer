# 使用指南 - 环境变量配置

## 配置方式

在 MCP 配置文件中通过 `env` 字段设置数据库连接信息。

### VS Code 配置

创建 `.vscode/mcp.json`:

```json
{
  "servers": {
    "DatabaseMcpServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "D:/Demo/my-mcp/DatabaseMcpServer"],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

### Visual Studio 配置

创建 `.mcp.json`:

```json
{
  "servers": {
    "DatabaseMcpServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "D:\\Demo\\my-mcp\\DatabaseMcpServer"],
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=mydb;User=root;Password=123456;",
        "DB_TYPE": "MySql"
      }
    }
  }
}
```

## 环境变量说明

| 变量名 | 必需 | 说明 | 示例 |
|--------|------|------|------|
| `DB_CONNECTION_STRING` | 是 | 数据库连接字符串 | `Server=localhost;Database=mydb;User=root;Password=123456;` |
| `DB_TYPE` | 否 | 数据库类型，默认 MySql | `MySql`, `SqlServer`, `Sqlite`, `PostgreSQL`, `Oracle` |

## 连接字符串示例

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

## 使用示例

配置完成后，所有工具都会自动使用环境变量中的数据库配置：

```
# 测试连接
"测试数据库连接"

# 列出所有表
"列出当前数据库的所有表"

# 查询数据
"查询 users 表的所有数据"

# 插入数据
"向 products 表插入数据: {\"name\":\"iPhone\",\"price\":5999}"
```

## 验证配置

使用 `get_database_config` 工具验证配置是否正确：

```
"获取数据库配置"
```

返回示例：
```json
{
  "configured": true,
  "databaseType": "MySql",
  "connectionString": "Server=localhost;Database=mydb;User=root;Password=****;",
  "message": "配置有效"
}
```
