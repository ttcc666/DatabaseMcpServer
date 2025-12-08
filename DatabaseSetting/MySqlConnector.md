# MySqlConnector 配置指南

MySqlConnector 复用 MySQL 配置与优化策略，使用官方 MySQL Connector 驱动。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "mysqlconnector-main",
      "connectionString": "Server=localhost;Port=3306;Database=demo;User Id=root;Password=pass;AllowLoadLocalInfile=true;",
      "dbType": "MySqlConnector",
      "description": "MySqlConnector 驱动示例",
      "isDefault": false,
      "optimizationSettings": {
        "enableBulkCopy": "true",
        "maxPoolSize": "100",
        "charset": "utf8mb4",
        "allowUserVariables": "true"
      }
    }
  ]
}
```

### MCP 配置

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

---

## 🚀 自动性能优化

- 复用 `MySqlOptimizationStrategy`：字符集/池化/BulkCopy/SSL/用户变量提示。
- BulkCopy 需连接串 `AllowLoadLocalInfile=true`。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `enableBulkCopy` | bool | 启用 BulkCopy（需 AllowLoadLocalInfile=true） |
| `maxPoolSize` | int | 连接池上限 |
| `charset` | string | 字符集，默认 utf8mb4 |
| `enableSsl` | bool | 启用 SSL |
| `allowUserVariables` | bool | 允许用户变量 |

---

## 🔗 参考资料

- [MySqlConnector（SqlSugar）](https://www.donet5.com/Doc/1/2412)

---

**最后更新**: 2025-12-08
