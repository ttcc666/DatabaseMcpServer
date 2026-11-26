# PolarDB 数据库配置指南

PolarDB 兼容 MySQL 协议，复用 MySQL 策略。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

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

- 复用 MySQL 协议驱动，每次操作独占客户端租约；能力按 PolarDB 产品声明判断。
- 池化、TLS、字符集使用连接字符串配置；MCP 没有 BulkCopy 执行入口。
- 若出现 `Unsupported command`，可在连接串设置 `Pooling=false`（禁用连接池）。
- 少数兼容场景可选 `disableNvarchar=true` 规避 `N''` 相关语法/索引问题。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `enableBulkCopy` | bool | not-applicable；不启用 BulkCopy |
| `maxPoolSize` | int | advisory；使用连接串 Max Pool Size |
| `charset` | string | advisory；使用连接串 Charset |
| `enableSsl` | bool | advisory；使用连接串 SslMode |
| `allowUserVariables` | bool | advisory；使用连接串 Allow User Variables |
| `disableNvarchar` | bool | 特殊环境禁用 `N''` 前缀时设为 `true` |

---

## 🔗 参考资料

- [PolarDB（SqlSugar）](https://www.donet5.com/Doc/1/2575)

---

**最后更新**: 2026-11-26
