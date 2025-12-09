# PolarDB 数据库配置指南

PolarDB 兼容 MySQL 协议，复用 MySQL 策略。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "polardb-main",
      "connectionString": "Server=polardb.host;Port=3306;Database=demo;Uid=root;Pwd=pass;",
      "dbType": "PolarDB",
      "description": "阿里云 PolarDB（MySQL 兼容）",
      "isDefault": false,
      "optimizationSettings": {
        "enableBulkCopy": "true",
        "maxPoolSize": "100",
        "charset": "utf8mb4",
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

- 复用 `MySqlOptimizationStrategy`：字符集/池化/BulkCopy/SSL/用户变量提示。
- 若需 BulkCopy，连接串需 `AllowLoadLocalInfile=true`。
- 若出现 `Unsupported command`，可在连接串设置 `Pooling=false`（禁用连接池）。
- 少数兼容场景可选 `disableNvarchar=true` 规避 `N''` 相关语法/索引问题。

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
| `disableNvarchar` | bool | 特殊环境禁用 `N''` 前缀时设为 `true` |

---

## 🔗 参考资料

- [PolarDB（SqlSugar）](https://www.donet5.com/Doc/1/2575)

---

**最后更新**: 2025-12-10
