# Vastbase 数据库配置指南

Vastbase 基于 PostgreSQL，需要在连接字符串添加 `No Reset On Close=true` 以避免会话重置问题。默认表名转小写，部分版本可选禁用 Nvarchar。

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
        "maxPoolSize": "100",
        "disableNvarchar": "false"
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

- `VastbaseOptimizationStrategy` 默认启用 `PgSqlIsAutoToLower=true`，支持 `autoToLower`、`autoToLowerCodeFirst` 与 `disableNvarchar`。
- 请在连接字符串中显式配置 `No Reset On Close=true` 以避免会话重置问题。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：使用 PostgreSQL 协议（无需额外包）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `autoToLower` | bool | `mapped`：表/列名自动转小写（默认 true） |
| `autoToLowerCodeFirst` | bool | `mapped`：CodeFirst 表/列名自动转小写 |
| `disableNvarchar` | bool | `mapped`：少数版本需禁用 `N''` 前缀时设为 `true` |
| `noResetOnClose` | bool | `advisory`：请在连接串设置 `No Reset On Close=true` |
| `maxPoolSize` | int | `advisory`：请在连接串配置连接池大小 |

---

## 🔧 版本/加密配置

- 版本要求：`SqlSugarCore` ≥ 5.1.4.111-preview06。
- 加密方式（.NET 连接报错时）：
  1. `password_encryption_type=1`（支持 sha256/md5）
  2. 重启服务
  3. 重置密码

详细说明：<https://www.donet5.com/ask/9/21149>

---

## 🔗 参考资料

- [Vastbase（SqlSugar）](https://www.donet5.com/Doc/1/2570)

---

**最后更新**: 2025-12-08
