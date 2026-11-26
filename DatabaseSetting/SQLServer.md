# SQL Server 数据库配置指南

本文档详细说明 Microsoft SQL Server 数据库的配置方法，涵盖驱动加密参数、.NET 全球化配置、NoLock 规范、禁用 nvarchar 等实战要点。

本服务执行原始 ADO/SqlQueryable SQL，enableNoLock 不会自动为这些查询添加 WITH(NOLOCK)。ORM 选项生效范围见 [统一配置约定](../Doc/orm-configuration.md)；未经本项目测量的性能比例不能视为承诺。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "connectionString": "Server=localhost;Database=myapp;User Id=sa;Password=YourStrong@Password;Encrypt=True;TrustServerCertificate=True;Min Pool Size=1;Max Pool Size=100;Connection Timeout=30;",
      "dbType": "SqlServer",
      "description": "SQL Server 主库（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableNoLock": "true",
        "disableNoLockWithTran": "true",
        "disableNvarchar": "true"
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
| `Server` | 服务器地址 | `localhost` | 可以是 IP、域名或实例名 |
| `Database` | 数据库名 | `myapp` | 要连接的数据库 |
| `User Id` | 用户名 | `sa` | 数据库用户 |
| `Password` | 密码 | `YourStrong@Password` | 用户密码 |

### TLS 参数

| 参数 | 说明 | 推荐值 | 备注 |
|------|------|--------|------|
| `Encrypt` | 启用加密 | `True` | 配合服务器 TLS 和证书配置 |
| `TrustServerCertificate` | 跳过证书校验 | `False` | True 仅用于受控开发环境 |

**注意**: 生产环境使用可信证书并校验服务器主机名。TrustServerCertificate=True 并非必需，不应作为所有连接问题的通用修复。

### .NET 全球化配置

如出现 `Only the invariant globalization mode is supported`，在启动项目的 `.csproj` 中设置：
```xml
<PropertyGroup>
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Min Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |
| `Pooling` | 启用连接池 | `true` | 默认启用，连接复用 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `MultipleActiveResultSets` | 多活动结果集 | `false` | 复杂查询设为 `true` |
| `Application Name` | 应用程序名称 | - | 便于监控和调试 |
| `Integrated Security` | Windows 身份验证 | `false` | 设为 `true` 使用 Windows 认证 |
| `Encrypt` / `TrustServerCertificate` | 加密/证书校验 | 按驱动版本核对 | 生产应验证可信证书 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 SQL Server 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| **NoLock 查询** | 仅实体 Queryable 的 ORM 选项；不改写 MCP 原始 SQL | 允许脏读，需要业务自行判断 |
| 事务中禁用 NoLock | 实体 Queryable 设置，不覆盖 SQL 中显式 hints | 不代替事务隔离级别 |
| 连接池复用 | 独立 SqlSugarClient + 驱动连接池 | 每次操作独占实例并自动关闭连接 |
| 可选禁用 nvarchar | 通过 `disableNvarchar` 配置 | ORM 参数策略；效果需用实际执行计划验证 |
| 原生连通性验证 | 建议失败时使用 `new SqlConnection(...).Open()` | 快速判定驱动/配置问题 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### disableNvarchar

**说明**: 禁用 nvarchar 参数，优化索引性能

**类型**: `boolean`

**默认值**: `false`

**推荐值**: `true`（当数据库使用 varchar 字段时）

**使用场景**:
- 数据库字段使用 `varchar` 而非 `nvarchar`
- 查询慢且索引未生效
- 执行计划显示 `N''` 参数导致索引失效

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "optimizationSettings": {
        "disableNvarchar": "true"
      }
    }
  ]
}
```

**作用范围**: ORM 生成路径；是否使用索引取决于类型、排序规则和执行计划。MCP 原始 SQL 参数需按实际字段选择 dbType，例如 String 或 AnsiString。

**性能对比**:
```sql
-- 未优化（使用 nvarchar 参数）
WHERE name = N'张三'  -- 索引可能失效

-- 优化后（使用 varchar 参数）
WHERE name = '张三'   -- 索引正常使用
```

---

### enableNoLock

**说明**: 控制实体 Queryable 的 WITH(NOLOCK) 选项（默认 true）；不改写本服务的原始 SQL 查询。

**类型**: `boolean`

**默认值**: `true`

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "optimizationSettings": {
        "enableNoLock": "false"
      }
    }
  ]
}
```

---

### disableNoLockWithTran

**说明**: 控制实体 Queryable 事务内是否禁用 NoLock（默认 true）；不代替事务隔离级别，也不移除 SQL 中显式编写的 hint。

**类型**: `boolean`

**默认值**: `true`

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "optimizationSettings": {
        "disableNoLockWithTran": "true"
      }
    }
  ]
}
```

---

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "sqlserver-prod",
      "connectionString": "Server=prod.sqlserver.com;Database=production;User Id=app_user;Password=SecureP@ssw0rd;Encrypt=True;TrustServerCertificate=False;Min Pool Size=5;Max Pool Size=200;Connection Timeout=30;Application Name=MyApp;",
      "dbType": "SqlServer",
      "description": "SQL Server 生产环境（高并发配置）",
      "isDefault": true,
      "optimizationSettings": {
        "disableNvarchar": "true"
      }
    }
  ]
}
```

**MCP 配置**:
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

**特点**:
- ✅ 大连接池（5-200）
- ✅ 安全证书验证
- ✅ 应用程序标识
- ORM NoLock 设置不作用于当前原始 SQL 查询
- ✅ 禁用 nvarchar 优化索引

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "sqlserver-dev",
      "connectionString": "Server=localhost;Database=dev_db;User Id=sa;Password=Dev@123456;Encrypt=True;TrustServerCertificate=True;Min Pool Size=1;Max Pool Size=50;",
      "dbType": "SqlServer",
      "description": "SQL Server 开发环境",
      "isDefault": true
    }
  ]
}
```

**特点**:
- ✅ 小连接池（1-50）
- ✅ 信任服务器证书（简化配置）
- ✅ 快速开发

### Windows 身份验证配置

```json
{
  "databases": [
    {
      "name": "sqlserver-windows",
      "connectionString": "Server=localhost;Database=myapp;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;",
      "dbType": "SqlServer",
      "description": "SQL Server Windows 身份验证",
      "isDefault": true
    }
  ]
}
```

---

## 🔍 常见问题

### 问题 1: TLS/SSL 连接错误

**错误**:
```
A connection was successfully established with the server, but then an error occurred during the login process
```

**排查**: 检查服务端证书、证书链、主机名、TLS 版本与 Encrypt 设置。生产环境配置可信证书，受控本地开发才可临时使用 TrustServerCertificate=True。

### 问题 1.1: 连接不上/偶发现象

**排查步骤**:
1) 使用原生方式验证：
```csharp
new SqlConnection("Server=...;...").Open();
```
2) 当前服务每次操作独占 `SqlSugarClient` 租约，物理连接由驱动池复用；结合 `config doctor`、`test_connection` 和请求日志检查连接池、超时及并发限制。
3) 检查 `Encrypt`、服务器证书和主机名是否匹配。生产环境应使用可信证书；`TrustServerCertificate=True` 仅适用于受控的本地开发环境，并非必需参数。

### 问题 2: 查询慢 - 索引失效

**现象**: 查询慢，执行计划显示索引扫描而非索引查找

**原因**: nvarchar 参数导致 varchar 字段索引失效

**解决方案**:
```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "optimizationSettings": {
        "disableNvarchar": "true"
      }
    }
  ]
}
```

**验证**:
```sql
-- 查看执行计划
SET STATISTICS IO ON;
SELECT * FROM users WHERE name = '张三';
```

### 问题 3: 连接池耗尽

**错误**:
```
Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool
```

**原因**: 连接池配置过小

**解决方案**:
```json
"connectionString": "...;Max Pool Size=200;..."
```

### 问题 4: .NET 全球化错误

**错误**:
```
Only the invariant globalization mode is supported
```

**解决方案**:
在项目 `.csproj` 文件中添加:
```xml
<PropertyGroup>
<InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

### 问题 5: NoLock 规范

- WITH(NOLOCK) 允许脏读，不是通用优化建议；本服务不自动添加。
- ORM 的 DisableWithNoLockWithTran 不能移除用户原始 SQL 中的显式 hints，也不保证业务一致性。

---

## 🎯 性能优化建议

### 1. NoLock 查询优化

**适用范围**: IsWithNoLockQuery 只影响实体 Queryable；DatabaseMcpServer 原始查询不自动添加此 hint。

**性能影响**: 需要结合实际负载测量。

**注意事项**:
- ✅ 适用于读多写少的场景
- 事务一致性取决于隔离级别和实际 SQL
- ⚠️ 可能读取到未提交的数据（脏读）

**手动控制**:
```csharp
// 实体 Queryable 显式启用 NoLock（允许脏读）
db.Queryable<Order>().With(SqlWith.NoLock).ToList();

// 实体 Queryable 全局禁用 NoLock
MoreSettings = new ConnMoreSettings() { IsWithNoLockQuery = false }
```

### 2. 索引优化

**问题**: varchar 字段使用 nvarchar 参数导致索引失效

**解决**: 在 databases.json 中启用 `disableNvarchar`

**配置示例**:
```json
{
  "databases": [
    {
      "name": "sqlserver-main",
      "optimizationSettings": {
        "disableNvarchar": "true"
      }
    }
  ]
}
```

**性能对比**:
- 未优化: 1000ms（索引扫描）
- 优化后: 50ms（索引查找）

### 3. 连接池配置

**低并发场景** (< 100 QPS):
```
Min Pool Size=1;Max Pool Size=50;
```

**中并发场景** (100-1000 QPS):
```
Min Pool Size=5;Max Pool Size=100;
```

**高并发场景** (> 1000 QPS):
```
Min Pool Size=10;Max Pool Size=200;
```

### 4. 异步性能优化

**建议**: 升级到 `Microsoft.Data.SqlClient` 最新版本

**性能提升**: 异步读写大文本/大文件性能显著提升

**注意**: 调试模式（F5）下异步较慢，使用 `Ctrl+F5` 或发布后测试

---

## 🌐 特殊类型支持

### Geometry 类型

```csharp
[SugarColumn(ColumnDataType = "geometry")]
public string Geometry1 { get; set; }

// 插入
db.Insertable(new UnitGe() { Geometry1 = "POINT (20 180)" }).ExecuteCommand();

// 查询
var list = db.Queryable<UnitGe>()
    .Select(it => new { Geometry1 = it.Geometry1.ToString() })
    .ToList();
```

### 表值参数（Table-Valued Parameters）

```csharp
var dt = new DataTable();
// ... 填充 DataTable

var param = new SugarParameter("@p", dt);
param.TypeName = "dtTableName";

db.Ado.ExecuteCommand("EXEC sp_name @p", param);
```

---

**最后更新**: 2026-11-26
