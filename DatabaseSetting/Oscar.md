# 神通数据库（Oscar）配置指南

Oscar 使用轻量优化策略，仅连接池提示。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "oscar-main",
      "connectionString": "Server=oscar.host:2003;User Id=sysdba;Password=pass;Database=demo;",
      "dbType": "Oscar",
      "description": "神通数据库",
      "isDefault": false,
      "optimizationSettings": {
        "maxPoolSize": "100"
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

- `OscarOptimizationStrategy`：保持 nvarchar 支持，记录连接池提示。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：安装神通数据库官方 .NET/ODBC 驱动。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [神通数据库（SqlSugar）](https://www.donet5.com/Doc/1/2369)

---

**最后更新**: 2025-12-08
