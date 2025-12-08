# DB2 数据库配置指南

DB2 使用轻量优化策略，仅记录连接池提示。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "db2-main",
      "connectionString": "Server=host:50000;Database=sample;UID=db2user;PWD=pass;",
      "dbType": "DB2",
      "description": "DB2 数据库",
      "isDefault": false,
      "optimizationSettings": {
        "maxPoolSize": "50"
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

- `Db2OptimizationStrategy`：保持 nvarchar 支持，仅提示连接池大小。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：需安装 IBM DB2 .NET/ODBC 驱动并正确配置数据源。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [DB2（SqlSugar）](https://www.donet5.com/Doc/1/2646)

---

**最后更新**: 2025-12-08
