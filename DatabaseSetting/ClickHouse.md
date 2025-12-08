# ClickHouse 数据库配置指南

ClickHouse 为列式分析数据库，SqlSugar 提供基础 CRUD/批量写入支持。需安装专用驱动包。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "clickhouse-main",
      "connectionString": "Host=localhost;Port=8123;User=default;Password=;Database=default",
      "dbType": "ClickHouse",
      "description": "ClickHouse 分析库",
      "isDefault": false,
      "optimizationSettings": {
        "maxPoolSize": "50"
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

- `ClickHouseOptimizationStrategy` 仅记录连接池提示，保持 `DisableNvarchar=false`。
- 不支持事务，大小写需与库一致；批量写入需升级驱动到新版。

---

## 📦 依赖要求

- NuGet: `SqlSugar.ClickHouseCore`（项目已引用）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数（提示用） |

---

## 🔗 参考资料

- [ClickHouse（SqlSugar）](https://www.donet5.com/Doc/1/2437)

---

**最后更新**: 2025-12-08
