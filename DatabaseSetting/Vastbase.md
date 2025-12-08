# Vastbase 数据库配置指南

Vastbase 基于 PostgreSQL，需要在连接字符串添加 `No Reset On Close=true` 以避免会话重置问题。默认表名转小写。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "vastbase-main",
      "connectionString": "PORT=5432;DATABASE=demo;HOST=localhost;USER ID=postgres;PASSWORD=pass;No Reset On Close=true",
      "dbType": "Vastbase",
      "description": "Vastbase 数据库",
      "isDefault": true,
      "optimizationSettings": {
        "autoToLower": "true",
        "noResetOnClose": "true",
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

- `VastbaseOptimizationStrategy` 默认启用 `PgSqlIsAutoToLower=true`，贴合 PG 风格。
- 未设置或关闭 `noResetOnClose` 时，会发出警告提示补充到连接字符串。
- 记录 `maxPoolSize` 便于池化调优。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：使用 PostgreSQL 协议（无需额外包）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `autoToLower` | bool | 表/列名自动转小写（默认 true） |
| `noResetOnClose` | bool | 建议 `true`，对应连接串 `No Reset On Close=true` |
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [Vastbase（SqlSugar）](https://www.donet5.com/Doc/1/2570)

---

**最后更新**: 2025-12-08
