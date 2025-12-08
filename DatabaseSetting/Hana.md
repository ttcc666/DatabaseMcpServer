# SAP HANA 数据库配置指南

HANA 采用轻量优化策略，仅记录连接池提示。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "hana-main",
      "connectionString": "Server=hana.host:39015;UserID=SYSTEM;Password=pass;DatabaseName=DEMO;",
      "dbType": "HANA",
      "description": "SAP HANA 数据库",
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

- `HanaOptimizationStrategy`：保持 nvarchar 支持，仅提示连接池大小。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 驱动：需安装 SAP HANA .NET Provider。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池上限 |

---

## 🔗 参考资料

- [Hana（SqlSugar）](https://www.donet5.com/Doc/1/2645)

---

**最后更新**: 2025-12-08
