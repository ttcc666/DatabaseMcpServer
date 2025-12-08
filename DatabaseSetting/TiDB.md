# TiDB 数据库配置指南

本文档详细说明 TiDB 分布式数据库的配置方法。TiDB 是兼容 MySQL 协议的分布式数据库。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "tidb-production",
      "connectionString": "Server=localhost;Port=4000;Database=myapp;User=root;Password=;Charset=utf8mb4;AllowLoadLocalInfile=true;Min Pool Size=5;Max Pool Size=100;Pooling=true;Connection Timeout=30;",
      "dbType": "Tidb",
      "description": "TiDB 生产环境（分布式事务优化）",
      "isDefault": true,
      "optimizationSettings": {
        "enableHints": "true",
        "pessimisticTxn": "true",
        "maxPoolSize": "100",
        "enableBulkCopy": "true"
      }
    }
  ]
}
```

#### MCP 配置

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

## 🔧 连接字符串参数详解

### 必需参数

| 参数 | 说明 | 示例 | 备注 |
|------|------|------|------|
| `Server` | TiDB Server 地址 | `localhost` | TiDB Server 默认监听地址 |
| `Port` | 端口号 | `4000` | TiDB 默认端口（兼容 MySQL 3306） |
| `Database` | 数据库名 | `myapp` | 要连接的数据库 |
| `User` / `Uid` | 用户名 | `root` | TiDB 用户 |
| `Password` / `Pwd` | 密码 | `` | 默认为空 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Charset` | 字符集 | `utf8mb4` | 支持完整 Unicode |
| `AllowLoadLocalInfile` | 启用批量导入 | `true` | 批量导入性能提升 **10-50 倍** |
| `Pooling` | 启用连接池 | `true` | 分布式环境下连接复用更重要 |
| `Min Pool Size` | 最小连接数 | `5` | 分布式环境建议适当增大 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 分布式环境可能需要更长超时 |

### TiDB 特定参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `tidb_txn_mode` | 事务模式 | `optimistic` | 设置为 `pessimistic` 启用悲观锁 |
| `tidb_isolation_read_engines` | 读取引擎 | `tikv,tiflash,tidb` | 指定查询引擎 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 TiDB 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| MySQL 兼容性 | 自动识别 MySQL 协议 | 无缝兼容 MySQL 驱动 |
| 连接池复用 | SqlSugarScope 自动管理 | 分布式环境下性能更稳定 |
| 字符集支持 | 自动识别 utf8mb4 | 避免乱码，支持 emoji |
| Optimizer Hints | 支持查询优化提示 | 手动优化查询计划 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### enableHints

**类型**: `bool`
**默认值**: `false`
**说明**: 启用 TiDB Optimizer Hints 支持，允许在 SQL 中使用 `/*+ ... */` 优化查询计划。

**配置示例**:
```json
{
  "optimizationSettings": {
    "enableHints": "true"
  }
}
```

**使用示例**:
```csharp
// 强制使用索引
db.Queryable<Order>()
  .Hints("/*+ USE_INDEX(order idx_create_time) */")
  .ToList();

// 强制使用 TiFlash 引擎
db.Queryable<Order>()
  .Hints("/*+ READ_FROM_STORAGE(TIFLASH[order]) */")
  .ToList();
```

### pessimisticTxn

**类型**: `bool`
**默认值**: `false`
**说明**: 启用悲观事务模式。TiDB 默认使用乐观锁，在高并发冲突场景下建议使用悲观锁。

**配置示例**:
```json
{
  "optimizationSettings": {
    "pessimisticTxn": "true"
  }
}
```

**适用场景**:
- 高并发写入冲突
- 需要 `SELECT FOR UPDATE` 语义
- 与 MySQL 事务行为保持一致

### maxPoolSize

**类型**: `int`
**默认值**: `100`
**说明**: 最大连接池大小。分布式环境下建议适当增大。

**配置示例**:
```json
{
  "optimizationSettings": {
    "maxPoolSize": "200"
  }
}
```

### enableBulkCopy

**类型**: `bool`
**默认值**: `false`
**说明**: 启用批量操作优化，适用于大数据导入场景。

### disableNvarchar

**类型**: `bool`
**默认值**: `false`
**说明**: 官方文档提示“个别特殊的数据库需要禁用 Nvarchar”。如遇特殊部署出现 `N''` 前缀兼容问题，可设置为 `true`。

**配置示例**:
```json
{
  "optimizationSettings": {
    "enableBulkCopy": "true"
  }
}
```

---

## 📊 配置场景

### 场景 1: 生产环境（高并发 OLTP）

```json
{
  "name": "tidb-production-oltp",
  "connectionString": "Server=tidb-server;Port=4000;Database=app;User=app_user;Password=strong_pwd;Charset=utf8mb4;Min Pool Size=10;Max Pool Size=200;Pooling=true;",
  "dbType": "Tidb",
  "optimizationSettings": {
    "pessimisticTxn": "true",
    "maxPoolSize": "200",
    "enableHints": "true"
  }
}
```

**特点**:
- 悲观事务减少冲突
- 大连接池应对高并发
- 支持手动优化查询

### 场景 2: 分析环境（OLAP / TiFlash）

```json
{
  "name": "tidb-analytics",
  "connectionString": "Server=tidb-server;Port=4000;Database=analytics;User=analyst;Password=pwd;Charset=utf8mb4;Connection Timeout=60;",
  "dbType": "Tidb",
  "optimizationSettings": {
    "enableHints": "true"
  }
}
```

**特点**:
- 长超时时间支持复杂查询
- Hints 指定 TiFlash 引擎
- 适合大数据分析

### 场景 3: 开发环境

```json
{
  "name": "tidb-dev",
  "connectionString": "Server=localhost;Port=4000;Database=test;User=root;Password=;Charset=utf8mb4;",
  "dbType": "Tidb",
  "optimizationSettings": {}
}
```

**特点**:
- 简化配置
- 默认优化策略
- 快速开发测试

---

## ❓ 常见问题

### Q1: TiDB 与 MySQL 的区别？

**A**: TiDB 兼容 MySQL 协议，但有以下区别：
- **分布式架构**: 支持水平扩展
- **默认乐观锁**: 与 MySQL 悲观锁不同
- **TiFlash 引擎**: 支持 HTAP（混合事务/分析）
- **Optimizer Hints**: 支持更多优化提示

### Q2: 如何启用悲观事务？

**A**: 两种方式：
1. **全局配置**: 在 `optimizationSettings` 中设置 `pessimisticTxn: "true"`
2. **会话级别**: 执行 `SET tidb_txn_mode = 'pessimistic';`

### Q3: 连接超时如何处理？

**A**: 分布式环境网络延迟较高，建议：
- 增大 `Connection Timeout` 至 30-60 秒
- 检查 TiDB Server 健康状态
- 确认网络连通性

### Q4: 如何使用 TiFlash 加速分析查询？

**A**: 使用 Optimizer Hints：
```csharp
db.Queryable<Order>()
  .Hints("/*+ READ_FROM_STORAGE(TIFLASH[order]) */")
  .ToList();
```

### Q5: 是否支持事务？

**A**: 完全支持 ACID 事务：
```csharp
db.Ado.BeginTran();
try
{
    // 业务逻辑
    db.Ado.CommitTran();
}
catch
{
    db.Ado.RollbackTran();
}
```

---

## 🔗 相关资源

- [TiDB 官方文档](https://docs.pingcap.com/zh/tidb/stable)
- [TiDB 性能调优](https://docs.pingcap.com/zh/tidb/stable/performance-tuning-overview)
- [SqlSugar 文档](https://www.donet5.com/Home/Doc)
- [主 README](../README.md)

---

## 📝 版本历史

- **v2.0.3** (2025-12-05): 添加 TiDB 专用优化策略
- **v1.0.0**: 初始版本，使用 MySQL 通用策略

---

**最后更新**: 2025-12-05
