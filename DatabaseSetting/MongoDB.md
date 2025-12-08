# MongoDB 数据库配置指南

MongoDB 为文档型数据库，SqlSugar 提供表达式支持但联表受限。需安装 Mongo 专用驱动包。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "mongo-main",
      "connectionString": "mongodb://user:pass@localhost:27017/demo?authSource=admin",
      "dbType": "MongoDb",
      "description": "MongoDB 文档库",
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

- `MongoDbOptimizationStrategy` 仅记录连接池提示；连接管理由驱动负责。
- 联表支持有限，优先使用单集合/嵌套文档模型。

---

## 📦 依赖要求

- NuGet: `SqlSugar.MongoDbCore`（项目已引用）

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数（提示用） |

---

## 🔗 参考资料

- [MongoDB（SqlSugar）](https://www.donet5.com/Doc/1/2651)

---

**最后更新**: 2025-12-08
