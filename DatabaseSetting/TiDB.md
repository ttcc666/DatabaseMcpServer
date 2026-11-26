# TiDB 数据库配置指南

本文档详细说明 TiDB 分布式数据库的配置方法。TiDB 是兼容 MySQL 协议的分布式数据库。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

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
      "isDefault": true
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
| `AllowLoadLocalInfile` | 原生导入选项 | 按需配置 | 不启用 MCP BulkCopy |
| `Pooling` | 启用连接池 | `true` | 分布式环境下连接复用更重要 |
| `Min Pool Size` | 最小连接数 | `5` | 分布式环境建议适当增大 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 分布式环境可能需要更长超时 |

### TiDB 数据库变量（不是连接串参数）

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `tidb_txn_mode` | 事务模式 | `optimistic` | 设置为 `pessimistic` 启用悲观锁 |
| `tidb_isolation_read_engines` | 读取引擎 | `tikv,tiflash,tidb` | 指定查询引擎 |

---

## 驱动与优化配置

dbType=Tidb 复用 MySQL 协议驱动，但能力按 TiDB 产品判断。当前不开放专用 query_table_page、insert_row、原子批次或类型化存储过程；通用 query_database / execute_command 可用性仍受权限限制。

| optimizationSettings 键 | 状态 | 作用 |
| --- | --- | --- |
| disableNvarchar | mapped，默认 false | 影响相应 ORM 生成路径，不重写原始 SQL |
| maxPoolSize | advisory | 在连接串设置 Max Pool Size |
| enableHints / pessimisticTxn / enableBulkCopy | not-applicable | 不添加 hints、不选择事务模式、不启用 BulkCopy |

tidb_txn_mode 等是数据库变量，并非连接字符串参数。事务模式由数据库管理配置决定；MCP 独立调用使用不同租约，不能依赖前一次调用的会话 SET 延续到下一次。

## 📊 配置场景

### 场景 1: 生产环境（高并发 OLTP）

```json
{
  "name": "tidb-production-oltp",
  "connectionString": "Server=tidb-server;Port=4000;Database=app;User=app_user;Password=strong_pwd;Charset=utf8mb4;Min Pool Size=10;Max Pool Size=200;Pooling=true;",
  "dbType": "Tidb"
}
```

**特点**:
- 连接串配置较大的连接池；需结合服务器容量验证
- 事务模式由数据库配置决定，MCP 不设置 pessimisticTxn

### 场景 2: 分析环境（OLAP / TiFlash）

```json
{
  "name": "tidb-analytics",
  "connectionString": "Server=tidb-server;Port=4000;Database=analytics;User=analyst;Password=pwd;Charset=utf8mb4;Connection Timeout=60;",
  "dbType": "Tidb"
}
```

**特点**:
- Connection Timeout 仅控制连接建立，查询另受 commandTimeout 和服务端上限约束
- TiFlash 路由由数据库配置和实际 SQL 决定

### 场景 3: 开发环境

```json
{
  "name": "tidb-dev",
  "connectionString": "Server=localhost;Port=4000;Database=test;User=root;Password=;Charset=utf8mb4;",
  "dbType": "Tidb"
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

**A**: 由数据库管理配置选择事务模式。optimizationSettings.pessimisticTxn 当前不执行；独立 MCP 调用不共享事务或会话状态。

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

**A**: TiDB 数据库支持事务，但当前 MCP 不开放 TiDB 的 atomic 批次。以下仅为应用直接使用 SqlSugar 的示例：
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

## 🔧 其他可选参数

### disableNvarchar

**说明**: 少数兼容场景不支持 `N''` 前缀时可禁用 Nvarchar。

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "optimizationSettings": {
    "disableNvarchar": "true"
  }
}
```

**使用场景**: 遇到 `N''` 语法或索引问题时开启。

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

**最后更新**: 2026-11-26
