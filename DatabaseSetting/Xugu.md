# 虚谷数据库（Xugu）配置指南

Xugu 使用轻量优化策略，仅记录连接池提示。需安装专用驱动包。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "xugu-main",
      "connectionString": "Server=localhost;Port=5138;Database=demo;Uid=sysdba;Pwd=pass;",
      "dbType": "Xugu",
      "description": "虚谷数据库",
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

- `XuguOptimizationStrategy`：保持 nvarchar 支持，仅记录连接池提示。

---

## 📦 依赖要求

- NuGet: `SqlSugar.XuguCoreNew`（项目已引用）
- 驱动：安装虚谷数据库客户端并配置。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [虚谷数据库（SqlSugar）](https://www.donet5.com/Doc/1/2640)

---

**最后更新**: 2025-12-08
