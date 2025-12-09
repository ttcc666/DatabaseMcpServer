# GoldenDB 数据库配置指南

GoldenDB 兼容 MySQL 协议，但官方建议禁用连接池（`Pooling=false`）。本指南提供 `databases.json` 示例与可选优化项。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "goldendb-main",
      "connectionString": "Server=golden.host;Port=3306;Database=demo;Uid=root;Pwd=pass;Pooling=false;",
      "dbType": "GoldenDB",
      "description": "GoldenDB 主库",
      "isDefault": true,
      "optimizationSettings": {
        "disablePooling": "true",
        "maxPoolSize": "50"
      }
    }
  ]
}
```

> **必填提示**：连接字符串务必包含 `Pooling=false`，否则可能出现兼容性问题。

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

- 使用 `GoldenDbOptimizationStrategy`，保持 `DisableNvarchar=false`（遵循 MySQL 兼容）。
- 若未显式设置 `disablePooling=true`，策略会发出警告，提醒在连接字符串关闭连接池。
- 记录 `maxPoolSize` 便于日志与调优。

### 客户端示例（必须禁用连接池）

```csharp
var db = new SqlSugarClient(new ConnectionConfig
{
    DbType = DbType.GoldenDB,
    ConnectionString = "Server=localhost;Database=SqlSugar4xTest;Uid=root;Pwd=haosql;Pooling=false;",
    IsAutoCloseConnection = true
    // 特殊环境如需禁用 Nvarchar，可在 optimizationSettings 配置 disableNvarchar
});
```

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）  
- 无额外驱动，沿用 MySQL 协议。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `disablePooling` | bool | 建议设为 `true`，与连接串 `Pooling=false` 保持一致 |
| `maxPoolSize` | int | 记录连接池上限，便于监控与日志 |

---

## 🔗 参考资料

- [GoldenDB 官方说明（SqlSugar）](https://www.donet5.com/Doc/1/2642)

---

**最后更新**: 2025-12-08
**最后更新**: 2025-12-10
