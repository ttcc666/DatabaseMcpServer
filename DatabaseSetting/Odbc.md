# ODBC 通用配置指南

ODBC 走默认优化策略，可用于 SqlSugar 未内置的数据库。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "odbc-main",
      "connectionString": "Dsn=mydsn;Uid=user;Pwd=pass;",
      "dbType": "Odbc",
      "description": "ODBC 通用数据源",
      "isDefault": false
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

- 使用 `DefaultOptimizationStrategy`（无额外优化项）。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 系统需配置对应数据库的 ODBC 驱动与 DSN。

---

## 🔗 参考资料

- [ODBC（SqlSugar）](https://www.donet5.com/Doc/1/2441)

---

**最后更新**: 2025-12-08
