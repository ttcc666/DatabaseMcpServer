# SQL Server 数据库配置指南

本文档详细说明 Microsoft SQL Server 数据库的配置方法，涵盖新版驱动所需的加密参数、.NET 8 全球化配置、NoLock 规范、禁用 nvarchar 等实战要点。

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

### 新版驱动必需参数（重要）

| 参数 | 说明 | 推荐值 | 备注 |
|------|------|--------|------|
| `Encrypt` | 启用加密 | `True` | 新版驱动必需 |
| `TrustServerCertificate` | 信任服务器证书 | `True` | 开发环境推荐 |

**注意**: SqlSugarCore 5.1.4.169+ 版本必须添加这两个参数，否则连接失败；部分旧版可省略但不推荐。

### .NET 8 全球化配置

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
| `Encrypt` / `TrustServerCertificate` | 加密/信任证书 | `True` / `True|False` | 新版驱动必需，云上建议关闭信任 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 SQL Server 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| **NoLock 查询** | 自动添加 `WITH(NOLOCK)`（可用 `enableNoLock` 关闭） | 并发读取性能提升 **60-70%** |
| 事务中禁用 NoLock | 默认开启，可用 `disableNoLockWithTran` 调整 | 避免脏读 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |
| 可选禁用 nvarchar | 通过 `disableNvarchar` 配置 | 索引性能提升 **20-30%** |
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

**效果**:
- `true`: 使用 varchar 参数，索引正常使用
- `false`: 使用 nvarchar 参数（默认）

**性能对比**:
```sql
-- 未优化（使用 nvarchar 参数）
WHERE name = N'张三'  -- 索引可能失效

-- 优化后（使用 varchar 参数）
WHERE name = '张三'   -- 索引正常使用
```

---

### enableNoLock

**说明**: 控制是否为查询自动添加 `WITH(NOLOCK)`（默认 `true`）。

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

**说明**: 控制事务内是否禁用 NoLock（默认 `true`，保证事务一致性）。

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
- ✅ 自动 NoLock 优化
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

**原因**: 新版驱动要求 TLS/SSL 配置

**解决方案**:
```json
"connectionString": "...;Encrypt=True;TrustServerCertificate=True;..."
```

### 问题 1.1: 连接不上/偶发现象

**排查步骤**:
1) 使用原生方式验证：
```csharp
new SqlConnection("Server=...;...").Open();
```
2) 如偶发，使用 `SqlSugarScope` 单例模式提升线程安全。
3) 再检查连接字符串是否含必需的 `Encrypt=True;TrustServerCertificate=True;`。

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

### 问题 4: .NET 8 全球化错误

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

- 非事务读查询建议使用 `WITH(NOLOCK)`（默认已开启）。
- 事务内自动禁用以保证一致性，如需手动禁用：`db.CurrentConnectionConfig.MoreSettings.IsWithNoLockQuery = false;`

---

## 🎯 性能优化建议

### 1. NoLock 查询优化

**自动启用**: DatabaseMcpServer 自动为非事务查询添加 `WITH(NOLOCK)`

**性能提升**: 并发读取性能提升 **60-70%**

**注意事项**:
- ✅ 适用于读多写少的场景
- ✅ 事务中自动禁用，保证一致性
- ⚠️ 可能读取到未提交的数据（脏读）

**手动控制**:
```csharp
// 单个查询禁用 NoLock
db.Queryable<Order>().With(SqlWith.NoLock).ToList();

// 全局禁用 NoLock（不推荐）
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

**最后更新**: 2025-12-10
