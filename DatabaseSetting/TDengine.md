# TDengine 数据库配置指南

本文档基于 SqlSugar 官方指引，为 TDengine 提供配置示例。TDengine 在 DatabaseMcpServer 中使用默认优化策略（无额外 `optimizationSettings` 选项），只需正确配置连接字符串和驱动。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "tdengine-main",
      "connectionString": "Data Source=localhost;DataBase=demo;Username=root;Password=taosdata;Port=6030;",
      "dbType": "TDengine",
      "description": "TDengine 时序数据库",
      "isDefault": true
    }
  ]
}
```

> 连接字符串格式取决于所用驱动（ODBC/RESTful）。请根据实际部署填写 `Data Source/Host`、`Port`、`Username`、`Password` 等参数。

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

- 使用 TDengine 专用策略，默认禁用 nvarchar，并记录连接池与批量写入提示（可选配置）。
- 依赖 SqlSugar 对 TDengine 的支持，建议保持驱动版本最新。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（项目已默认引用）
- 驱动：根据 TDengine 部署选择官方 ODBC/RESTful 驱动

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数，便于高并发调优 |
| `batchSize` | int | 批量写入批次大小提示，用于日志观察与调优 |

---

## 🔗 参考资料

- [TDengine 官方文档](https://www.taosdata.com/docs/)
- [SqlSugar TDengine 支持](https://www.donet5.com/Doc/1/2566)

---

**最后更新**: 2025-12-08
