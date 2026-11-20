# MongoDB 数据库配置指南

MongoDB 为文档型数据库。发布包包含 SqlSugar.MongoDbCore 和官方 MongoDB.Driver，无需额外安装 .NET 驱动。

**当前 MCP 边界**：支持集合发现、find/count、insert one、update/delete，使用真实 JSON / Extended JSON v2。通用 SQL、聚合、任意 runCommand、索引及事务工具不开放。见[集合契约](../Doc/mongodb-collections.md)和 [ORM 配置约定](../Doc/orm-configuration.md)。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "mongo-main",
      "connectionString": "mongodb://user:pass@localhost:27017/demo?authSource=admin&maxPoolSize=100",
      "dbType": "MongoDb",
      "description": "MongoDB 文档库",
      "isDefault": false,
      "accessMode": "read-write",
      "enableDangerousOperations": false
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

- NuGet: `SqlSugar.MongoDbCore 5.1.4.280`、`MongoDB.Driver 3.11.1`（显式依赖）。
- URI 必须包含业务数据库 `/demo`，`authSource` 仅选择认证库。`mongodb+srv://` 原样交给官方驱动。
- TLS、连接池及 retryWrites 使用 URI 选项。写工具拒绝 `w=0`；many 和空 filter 需要额外显式授权。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数（提示用） |

---

## 🔗 参考资料

- [MongoDB（SqlSugar）](https://www.donet5.com/Doc/1/2651)

---

**最后更新**: 2026-11-20
