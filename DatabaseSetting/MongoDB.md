# MongoDB 数据库配置指南

MongoDB 为文档型数据库。当前源码通过 SqlSugar.MongoDbCore 内置 ADO 及其传递驱动依赖接入，无需额外安装 .NET 驱动。本文描述[5.0.0](../Doc/release-5.0.0.md)契约。

**当前 MCP 边界**：提供 `mongo_find`、`mongo_aggregate`、`mongo_insert_many`、`mongo_update`、`mongo_delete`、`mongo_truncate` 六个工具。输入使用 JSON / Extended JSON v2，输出为 provider 转换后的 JSON，不保证 BSON 无损往返。集合枚举、独立 count、集合元数据、explain、索引管理、事务和通用 SQL 工具不开放。见[集合契约](../Doc/mongodb-collections.md)和 [ORM 配置约定](../Doc/orm-configuration.md)。

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

## 执行与权限

- `MongoDbOptimizationStrategy` 仅记录连接池提示；操作复用 SqlSugar 客户端租约和全局/单连接准入。
- 上述示例允许读取与插入。update/delete 始终批量操作，需额外启用 `enableDangerousOperations=true`，空 filter 还要求 `allowAll=true`；truncate 要求 schema-admin 和危险操作开关。
- 读取使用内置 aggregate，服务器限制行数，provider 整批物化后再检查响应字节预算。初始同步请求可能延迟取消，执行槽保留到清理完成。
- 只读聚合支持同库 lookup/facet，具体 stage、嵌套和输入限制见集合契约；结果预算不限制服务器中间计算量。

---

## 📦 依赖要求

- 直接引用 `SqlSugar.MongoDbCore 5.1.4.280`，MongoDB.Driver 由该 provider 的依赖链带入，不再单独固定直接引用。
- URI 必须包含业务数据库 `/demo`，`authSource` 仅选择认证库。当前 provider 不支持 `mongodb+srv://`，配置校验明确拒绝；请使用集群提供的显式主机 `mongodb://` URI。
- TLS、连接池及 retryWrites 使用 URI 选项；部署环境需要单独验证。写工具拒绝 `w=0`；many/upsert 参数已移除，写后检查 writeOutcome，取消或 unknown 不代表回滚。

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数（提示用） |

---

## 🔗 参考资料

- [MongoDB（SqlSugar）](https://www.donet5.com/Doc/1/2651)

---

**最后更新**: 2026-11-26
