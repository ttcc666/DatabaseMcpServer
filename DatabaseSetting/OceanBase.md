# OceanBase 数据库配置指南

本文档详细说明 OceanBase 数据库的配置方法。OceanBase 支持 **MySQL 模式** 和 **Oracle 模式** 两种兼容模式。

---

## 📋 配置方式

### 1. OceanBase MySQL 模式

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "oceanbase-mysql",
      "connectionString": "Server=localhost;Port=2881;Database=test;User=root@sys;Password=password;Charset=utf8mb4;Min Pool Size=5;Max Pool Size=100;Pooling=true;Connection Timeout=30;",
      "dbType": "OceanBase",
      "description": "OceanBase MySQL 模式（租户隔离 + 分布式事务）",
      "isDefault": true,
      "optimizationSettings": {
        "enableHints": "true",
        "maxPoolSize": "100",
        "tenantMode": "mysql"
      }
    }
  ]
}
```

### 2. OceanBase Oracle 模式

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "oceanbase-oracle",
      "connectionString": "Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=XIR_TRD;User=XIR_TRD@Xpia2C6G#obtest:1650773680;Password=strong_pwd;Option=3;",
      "dbType": "OceanBaseForOracle",
      "description": "OceanBase Oracle 模式（ODBC 连接）",
      "optimizationSettings": {
        "camelCase": "false",
        "enableIdentity": "true"
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

## 🔧 MySQL 模式 - 连接字符串参数详解

### 必需参数

| 参数 | 说明 | 示例 | 备注 |
|------|------|------|------|
| `Server` | OBServer 地址 | `localhost` | OBProxy 或 OBServer 地址 |
| `Port` | 端口号 | `2881` | OBProxy 默认 2883，直连 OBServer 默认 2881 |
| `Database` | 数据库名 | `test` | 要连接的数据库 |
| `User` | 用户名@租户 | `root@sys` | 格式: `用户名@租户名` 或 `用户名@租户名#集群名` |
| `Password` | 密码 | `password` | 用户密码 |

### 租户连接格式

| 连接方式 | 格式 | 示例 |
|---------|------|------|
| 通过 OBProxy | `用户名@租户名` | `root@sys` |
| 直连 OBServer | `用户名@租户名#集群名` | `admin@tenant1#cluster1` |
| 指定资源组 | `用户名@租户名#集群名:资源组ID` | `user@tenant#cluster:1650773680` |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Charset` | 字符集 | `utf8mb4` | 支持完整 Unicode |
| `Pooling` | 启用连接池 | `true` | **注意**: 某些 OceanBase 服务器不支持连接池 |
| `Min Pool Size` | 最小连接数 | `5` | 保持最小连接 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 分布式环境建议适当增大 |

### 特殊场景参数

| 参数 | 说明 | 使用场景 |
|------|------|---------|
| `Pooling=false` | 禁用连接池 | **重要**: 如果连续执行两次写入操作报错，说明服务器不支持连接池，需添加此参数 |

---

## 🔧 Oracle 模式 - 连接字符串参数详解

### 前提条件

1. **安装 ODBC 驱动**: `ob-connector-odbc-2.0.8.2-win64.msi`
2. **SqlSugar 版本要求**: 5.1.4.92-preview14+

### 必需参数

| 参数 | 说明 | 示例 |
|------|------|------|
| `Driver` | ODBC 驱动 | `{OceanBase ODBC 2.0 Driver}` |
| `Server` | 服务器地址 | `172.19.9.9` |
| `Port` | 端口号 | `2883` |
| `Database` | 数据库名 | `XIR_TRD` |
| `User` | 完整用户名 | `XIR_TRD@Xpia2C6G#obtest:1650773680` |
| `Password` | 密码 | `strong_pwd` |
| `Option` | ODBC 选项 | `3` |

### 代码初始化（Oracle 模式）

```csharp
// 程序启动时执行一次（注册 OceanBase for Oracle 提供程序）
InstanceFactory.CustomAssemblies = new System.Reflection.Assembly[]
{
    typeof(OceanBaseForOracleProvider).Assembly
};

// 创建数据库连接
SqlSugarClient db = new SqlSugarClient(new ConnectionConfig()
{
    DbType = DbType.OceanBaseForOracle,
    ConnectionString = "Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;...",
    IsAutoCloseConnection = true
});
```

---

## 🚀 自动性能优化

### MySQL 模式优化

DatabaseMcpServer 自动为 OceanBase MySQL 模式应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| MySQL 兼容性 | 自动识别 MySQL 协议 | 无缝兼容 MySQL 驱动 |
| 连接池管理 | 智能连接池复用 | 可选禁用连接池（不兼容服务器） |
| 租户隔离 | 支持租户级别连接 | 多租户环境下资源隔离 |
| Optimizer Hints | 支持查询优化提示 | 手动优化查询计划 |

### Oracle 模式优化

自动应用 Oracle 兼容优化策略（详见 `Oracle.md`）。

---

## 🔧 MySQL 模式 - 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### disablePooling

**类型**: `bool`
**默认值**: `false`
**说明**: 禁用连接池。某些 OceanBase 服务器不支持连接池，连续执行两次写入操作会报错。

**配置示例**:
```json
{
  "optimizationSettings": {
    "disablePooling": "true"
  }
}
```

**或在连接字符串中设置**:
```json
{
  "connectionString": "Server=localhost;...;Pooling=false;"
}
```

### enableHints

**类型**: `bool`
**默认值**: `false`
**说明**: 启用 OceanBase Optimizer Hints 支持。

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
  .Hints("/*+ INDEX(order idx_create_time) */")
  .ToList();
```

### maxPoolSize

**类型**: `int`
**默认值**: `100`
**说明**: 最大连接池大小。

**配置示例**:
```json
{
  "optimizationSettings": {
    "maxPoolSize": "200"
  }
}
```

### tenantMode

**类型**: `string`
**默认值**: `"mysql"`
**说明**: 租户兼容模式（mysql / oracle）。

**配置示例**:
```json
{
  "optimizationSettings": {
    "tenantMode": "mysql"
  }
}
```

### enableBulkCopy

**类型**: `bool`
**默认值**: `false`
**说明**: 启用批量操作优化。

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

### 场景 1: MySQL 模式 - 生产环境（通过 OBProxy）

```json
{
  "name": "oceanbase-prod-mysql",
  "connectionString": "Server=obproxy.example.com;Port=2883;Database=app;User=app_user@tenant_prod;Password=strong_pwd;Charset=utf8mb4;Min Pool Size=10;Max Pool Size=200;Pooling=true;",
  "dbType": "OceanBase",
  "optimizationSettings": {
    "enableHints": "true",
    "maxPoolSize": "200",
    "tenantMode": "mysql"
  }
}
```

**特点**:
- 通过 OBProxy 连接（高可用）
- 大连接池应对高并发
- 支持手动优化查询

### 场景 2: MySQL 模式 - 不支持连接池的服务器

```json
{
  "name": "oceanbase-no-pool",
  "connectionString": "Server=localhost;Port=2881;Database=test;User=root@sys;Password=pwd;Pooling=false;",
  "dbType": "OceanBase",
  "optimizationSettings": {
    "disablePooling": "true"
  }
}
```

**特点**:
- 禁用连接池避免错误
- 适用于连续写入场景
- 简化配置

### 场景 3: Oracle 模式 - 生产环境

```json
{
  "name": "oceanbase-prod-oracle",
  "connectionString": "Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=PROD_DB;User=PROD_USER@TENANT#CLUSTER:123456;Password=strong_pwd;Option=3;",
  "dbType": "OceanBaseForOracle",
  "optimizationSettings": {
    "camelCase": "false",
    "enableIdentity": "true"
  }
}
```

**特点**:
- ODBC 连接
- Oracle 兼容模式
- 支持原生自增

---

## ❓ 常见问题

### Q1: 连续执行两次写入操作报错？

**A**: 这是 OceanBase 服务器不支持连接池导致的。解决方案：
1. **方案 1**: 在连接字符串中添加 `Pooling=false`
2. **方案 2**: 在 `optimizationSettings` 中设置 `disablePooling: "true"`

### Q2: 租户连接格式错误？

**A**: 检查连接方式：
- **通过 OBProxy**: `用户名@租户名`
- **直连 OBServer**: `用户名@租户名#集群名`
- **指定资源组**: `用户名@租户名#集群名:资源组ID`

### Q3: MySQL 模式和 Oracle 模式如何选择？

**A**:
- **MySQL 模式**: 使用 MySQL 协议，兼容 MySQL 语法，推荐新项目使用
- **Oracle 模式**: 使用 ODBC 协议，兼容 Oracle 语法，适用于 Oracle 迁移项目

### Q4: Oracle 模式的 varchar2 字段问题？

**A**: ODBC 无法配置 DbType 为 varchar2，建议：
- 数据库字段使用 `varchar` 而非 `varchar2`
- 或在应用层进行类型映射

### Q5: 如何使用雪花 ID 作为主键？

**A**: 推荐使用雪花 ID：
```csharp
long id = db.Insertable(entity).ExecuteReturnSnowflakeId();
```

### Q6: Oracle 模式的自增字段如何处理？

**A**:
- **Oracle 12C+**: 支持原生自增（设置 `enableIdentity: "true"`）
- **Oracle 11**: 使用序列 + 触发器
- **推荐**: 使用雪花 ID 避免并发问题

---

## 🔗 相关资源

- [OceanBase 官方文档](https://www.oceanbase.com/docs)
- [OceanBase MySQL 模式](https://www.oceanbase.com/docs/common-oceanbase-database-cn-10000000001577429)
- [OceanBase Oracle 模式](https://www.oceanbase.com/docs/common-oceanbase-database-cn-10000000001577430)
- [SqlSugar OceanBase 文档](https://www.donet5.com/Home/Doc?typeId=1274)
- [主 README](../README.md)

---

## 📦 依赖要求

### MySQL 模式
- **NuGet**: `SqlSugarCore`（已包含在项目中）
- **无需额外驱动**

### Oracle 模式
- **NuGet**: `SqlSugar.OceanBaseForOracleCore`（需要手动安装）
- **ODBC 驱动**: `ob-connector-odbc-2.0.8.2-win64.msi`
- **SqlSugar 版本**: 5.1.4.92-preview14+

---

## 📝 版本历史

- **v2.0.0** (2025-12-05): 添加 OceanBase MySQL 模式专用优化策略
- **v1.0.0**: 初始版本，支持 MySQL 模式和 Oracle 模式

---

**最后更新**: 2025-12-05
