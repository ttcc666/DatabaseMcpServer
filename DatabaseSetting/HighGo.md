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
      "connectionString": "Server=localhost;Port=5866;UId=design;Password=000;Database=design;searchpath=design;Pooling=false;",
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

- `HighGoOptimizationStrategy`：默认 `PgSqlIsAutoToLower=true`，支持 `autoToLower` 与 `autoToLowerCodeFirst`；`Pooling=false` 需在连接字符串中配置。

---

## 📦 依赖要求

- 已包含 `SqlSugarCore 5.1.4.221`、`SqlSugar.HGCore 5.1.4.211`，传递依赖 `SqlSugar.Nhgdb 5.1.4.159`（`Nhgdb.dll`），由服务统一注册扩展。
- `HG` 使用瀚高专用驱动，不等同于直接选择 PostgreSQL。实际服务器版本、认证及 searchpath 需要在部署环境验证，见[驱动清单](../Doc/provider-drivers.md)。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `autoToLower` | bool | `mapped`：表/列名自动转小写（默认 true） |
| `autoToLowerCodeFirst` | bool | `mapped`：CodeFirst 表/列名自动转小写 |
| `disablePooling` | bool | `advisory`：请在连接串设置 `Pooling=false` |
| `maxPoolSize` | int | `advisory`：请在连接串配置连接池大小 |

---

## 🔗 参考资料

- [瀚高数据库（SqlSugar）](https://www.donet5.com/Doc/1/2436)

---

**最后更新**: 2025-12-10
