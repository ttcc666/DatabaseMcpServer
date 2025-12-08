# DuckDB 数据库配置指南

DuckDB 是内嵌列式数据库，SqlSugar 提供基础 CRUD 支持，优化项仅连接池提示。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "duckdb-main",
      "connectionString": "Data Source=./data/demo.duckdb;",
      "dbType": "DuckDB",
      "description": "DuckDB 内嵌列式库",
      "isDefault": false,
      "optimizationSettings": {
        "maxPoolSize": "10"
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

- `DuckDbOptimizationStrategy`：保持 nvarchar 支持，仅记录连接池提示。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 无额外驱动，数据库文件路径可本地或内存。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池上限（内嵌场景通常较小） |

---

## 🔗 参考资料

- [DuckDB（SqlSugar）](https://www.donet5.com/Doc/1/2647)

---

**最后更新**: 2025-12-08
