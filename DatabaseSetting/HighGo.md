# 瀚高数据库（HighGo）配置指南

HighGo 基于 PostgreSQL，默认表/列名转小写。**建议连接字符串禁用连接池 `Pooling=false`，并配置架构 `searchpath=`**。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "highgo-main",
      "connectionString": "Server=27.151.1.54;Port=5866;UId=design;Password=000;Database=design;searchpath=design;Pooling=false;",
      "dbType": "HG",
      "description": "瀚高数据库",
      "isDefault": false,
      "optimizationSettings": {
        "autoToLower": "true",
        "maxPoolSize": "100",
        "disablePooling": "true"
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

- `HighGoOptimizationStrategy`：默认 `PgSqlIsAutoToLower=true`；未配置或未禁用连接池时会提示设置 `Pooling=false`。记录 `disablePooling/maxPoolSize` 便于调优。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：使用 PostgreSQL 协议。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `autoToLower` | bool | 表/列名自动转小写（默认 true） |
| `disablePooling` | bool | 建议 `true`，对应连接串 `Pooling=false` |
| `maxPoolSize` | int | 连接池上限（如已禁用池可忽略） |

---

## 🔗 参考资料

- [瀚高数据库（SqlSugar）](https://www.donet5.com/Doc/1/2436)

---

**最后更新**: 2025-12-10
