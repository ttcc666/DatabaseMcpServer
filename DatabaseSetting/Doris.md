# Doris 数据库配置指南

Doris 兼容 MySQL 协议，建议连接串禁用连接池（`Pooling=false`），策略复用 MySQL。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "doris-main",
      "connectionString": "Server=doris.host;Port=9030;Database=demo;Uid=root;Pwd=pass;Pooling=false;",
      "dbType": "Doris",
      "description": "Doris（MySQL 兼容）",
      "isDefault": false,
      "optimizationSettings": {
        "enableBulkCopy": "true",
        "maxPoolSize": "100",
        "charset": "utf8mb4"
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
- Doris 文档建议连接串 `Pooling=false`，可与优化项配合。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `enableBulkCopy` | bool | 启用 BulkCopy（需连接串 AllowLoadLocalInfile=true） |
| `maxPoolSize` | int | 连接池上限 |
| `charset` | string | 字符集，默认 utf8mb4 |
| `enableSsl` | bool | 启用 SSL |
| `allowUserVariables` | bool | 允许用户变量 |

---

## 🔗 参考资料

- [Doris（SqlSugar）](https://www.donet5.com/Doc/1/2577)

---

**最后更新**: 2025-12-08
