# 瀚高数据库（HighGo）配置指南

HighGo 基于 PostgreSQL，默认表/列名转小写，可选连接池配置。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "highgo-main",
      "connectionString": "PORT=5866;DATABASE=demo;HOST=localhost;USER ID=hg;PASSWORD=pass;",
      "dbType": "HG",
      "description": "瀚高数据库",
      "isDefault": false,
      "optimizationSettings": {
        "autoToLower": "true",
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

- `HighGoOptimizationStrategy`：默认 `PgSqlIsAutoToLower=true`，可按需关闭；记录连接池提示。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：使用 PostgreSQL 协议。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `autoToLower` | bool | 表/列名自动转小写（默认 true） |
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [瀚高数据库（SqlSugar）](https://www.donet5.com/Doc/1/2436)

---

**最后更新**: 2025-12-08
