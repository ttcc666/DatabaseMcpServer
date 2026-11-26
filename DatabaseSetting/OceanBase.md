# OceanBase 数据库配置指南

本文档详细说明 OceanBase 数据库的配置方法。OceanBase 支持 **MySQL 模式** 和 **Oracle 模式** 两种兼容模式。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

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
      "isDefault": true
    }
  ]
}
```

### 2. OceanBase Oracle 模式

> **提示**：Oracle 模式需要单独配置，不要复用 MySQL 模式的连接串或 `dbType`。

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

#### 🔢 Oracle 模式快速步骤

1. **NuGet 升级**：`SqlSugar.OceanBaseForOracleCore` 升级到 `5.1.4.92-preview14+`（项目已内置 5.1.4.205），同时保留 `SqlSugarCore`。
2. **安装 ODBC 驱动**：`ob-connector-odbc-2.0.8.2-win64.msi`。
3. **创建连接串**（Oracle/ODBC）：  
   `Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=XIR_TRD;User=XIR_TRD@Xpia2C6G#obtest:1650773680;Password=aaAA11%%;Option=3;`
4. **程序启动预热一次**：
   ```csharp
   InstanceFactory.CustomAssemblies = new System.Reflection.Assembly[]
   {
       typeof(OceanBaseForOracleProvider).Assembly
   };
   ```
5. **构建客户端**：
   ```csharp
   SqlSugarClient db = new SqlSugarClient(new ConnectionConfig()
   {
       DbType = DbType.OceanBaseForOracle,
       ConnectionString = "Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=XIR_TRD;User=XIR_TRD@Xpia2C6G#obtest:1650773680;Password=aaAA11%%;Option=3;",
       IsAutoCloseConnection = true
       // 个别环境如需可禁用 Nvarchar
       // MoreSettings = new ConnMoreSettings { DisableNvarchar = true }
   });
   ```
6. **自增策略**：建议使用雪花 ID；如必须用 Oracle 自增，参考 [Oracle 自增文档](https://www.donet5.com/Home/Doc?typeId=1220)（并发高需自行加锁）。
7. **字段类型**：ODBC 无法声明 `varchar2` 类型，建议表字段使用 `varchar`。

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

## 驱动与优化配置

dbType=OceanBase 选择 MySQL 模式；dbType=OceanBaseForOracle 选择 Oracle 模式及对应驱动。tenantMode 不切换驱动，连接串须匹配服务端租户。当前兼容产品的专用分页、自增 insert_row 和原子批次没有开放，以能力查询为准。

| optimizationSettings 键 | 状态 | 作用 |
| --- | --- | --- |
| disableNvarchar | mapped | 影响相应 ORM 生成路径，不改写原始 SQL |
| disablePooling / maxPoolSize | advisory | 通过连接串 Pooling / Max Pool Size 配置 |
| tenantMode | advisory | 保留诊断；以 dbType 和连接串选择模式 |
| enableHints / enableBulkCopy | not-applicable | 不自动添加 hints 或启用 BulkCopy |

Oracle 模式复用对应 ORM 配置项，见 [Oracle 指南](Oracle.md)。每次操作独占客户端租约，不能依赖独立工具调用间保留会话状态。

## 📊 配置场景

### 场景 1: MySQL 模式 - 生产环境（通过 OBProxy）

```json
{
  "name": "oceanbase-prod-mysql",
  "connectionString": "Server=obproxy.example.com;Port=2883;Database=app;User=app_user@tenant_prod;Password=strong_pwd;Charset=utf8mb4;Min Pool Size=10;Max Pool Size=200;Pooling=true;",
  "dbType": "OceanBase"
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
  "dbType": "OceanBase"
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

**A**: 先核对驱动、租户配置和服务器错误。若已确认目标不兼容连接池，可在连接字符串设置 Pooling=false；optimizationSettings.disablePooling 仅为 advisory，不会禁用连接池。

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
- **NuGet**: `SqlSugar.OceanBaseForOracleCore`（项目默认引用，版本 ≥ 5.1.4.92-preview14）
- **ODBC 驱动**: `ob-connector-odbc-2.0.8.2-win64.msi`
- **SqlSugar 版本**: 5.1.4.92-preview14+

---

## 📝 版本历史

- **v2.0.0** (2025-12-05): 添加 OceanBase MySQL 模式专用优化策略
- **v1.0.0**: 初始版本，支持 MySQL 模式和 Oracle 模式

---

**最后更新**: 2026-11-26
