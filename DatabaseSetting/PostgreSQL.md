# PostgreSQL 数据库配置指南

本文档详细说明 PostgreSQL 数据库的配置方法。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "postgres-main",
      "connectionString": "Host=localhost;Port=5432;Database=myapp;Username=postgres;Password=postgres123;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=30;Command Timeout=30;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL 主库（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "autoToLower": "true",
        "enableILike": "true",
        "identityStrategy": "Serial"
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
| `Host` | 服务器地址 | `localhost` | 可以是 IP 或域名 |
| `Port` | 端口号 | `5432` | PostgreSQL 默认端口 |
| `Database` | 数据库名 | `myapp` | 要连接的数据库 |
| `Username` | 用户名 | `postgres` | 数据库用户 |
| `Password` | 密码 | `postgres123` | 用户密码 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Pooling` | 启用连接池 | `true` | 连接复用，性能提升 **80%+** |
| `Minimum Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Maximum Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |
| `Command Timeout` | 命令超时（秒） | `30` | 避免长查询阻塞 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `SSL Mode` | SSL 模式 | `Disable` | 安全连接：`Require` |
| `Search Path` | 架构搜索路径 | `public` | 多 schema：`schema1,schema2` |
| `Application Name` | 应用程序名称 | - | 便于监控和调试 |
| `Keepalive` | 保持连接活跃（秒） | `0` | 长连接：`60` |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 PostgreSQL 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| **表名自动转小写** | 规范化表名 | 避免大小写问题 |
| ILike 支持 | 不区分大小写查询 | 查询灵活性提升 |
| 自增策略 | Serial/Identity 可选 | 兼容不同版本 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### autoToLower

**说明**: 表名自动转小写（规范化）

**类型**: `boolean`

**默认值**: `true`（推荐）

**示例**:
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "autoToLower": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 表名自动转小写（默认，推荐）
- `false`: 保持原样，需要双引号

**对比**:
```sql
-- autoToLower=true（默认，推荐）
SELECT * FROM userinfo  -- 自动转小写

-- autoToLower=false
SELECT * FROM "UserInfo"  -- 保持原样，需要双引号
```

---

### enableILike

**说明**: 启用 ILike 不区分大小写查询

**类型**: `boolean`

**默认值**: `false`（不启用）

**示例**:
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "enableILike": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 启用 ILike 不区分大小写查询
- `false`: 使用标准 LIKE（区分大小写）

**对比**:
```sql
-- 未启用（区分大小写）
WHERE name LIKE '%Test%'  -- 只匹配 Test

-- 启用 ILike（不区分大小写）
WHERE name ILIKE '%test%'  -- 匹配 Test, TEST, test
```

---

### identityStrategy

**说明**: 自增策略选择

**类型**: `string`

**默认值**: `Serial`（兼容低版本）

**可选值**:
- `Serial` - 兼容 PostgreSQL 所有版本
- `Identity` - PostgreSQL 10+ 推荐

**示例**:
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "identityStrategy": "Identity"
      }
    }
  ]
}
```

**使用场景**:
- `Serial`: PostgreSQL 9.x 及以下
- `Identity`: PostgreSQL 10+ 推荐使用

**对比**:

**Serial（传统方式）**:
```sql
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100)
);
```

**Identity（PostgreSQL 10+）**:
```sql
CREATE TABLE users (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(100)
);
```

---

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "postgres-prod",
      "connectionString": "Host=prod.postgres.com;Port=5432;Database=production;Username=app_user;Password=SecureP@ssw0rd;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=200;Timeout=30;Command Timeout=30;SSL Mode=Require;Application Name=MyApp;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL 生产环境（高并发配置）",
      "isDefault": true,
      "optimizationSettings": {
        "autoToLower": "true",
        "enableILike": "true",
        "identityStrategy": "Identity"
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
- ✅ SSL 安全连接
- ✅ 应用程序标识
- ✅ PostgreSQL 10+ Identity

---

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "postgres-dev",
      "connectionString": "Host=localhost;Port=5432;Database=dev_db;Username=postgres;Password=postgres123;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=50;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL 开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "autoToLower": "true"
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
- ✅ 小连接池（1-50）
- ✅ 简化配置
- ✅ 快速开发

---

### 多 Schema 配置

```json
{
  "databases": [
    {
      "name": "postgres-multi-schema",
      "connectionString": "Host=localhost;Port=5432;Database=myapp;Username=postgres;Password=postgres123;Search Path=app_schema,public;Pooling=true;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL 多 Schema 配置",
      "isDefault": true,
      "optimizationSettings": {
        "autoToLower": "true",
        "enableILike": "true"
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

---

## 🔍 常见问题

### 问题 1: 表名找不到

**错误**:
```
relation "MyTable" does not exist
```

**原因**: PostgreSQL 默认转小写，但查询使用大写

**解决方案**:
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "autoToLower": "true"
      }
    }
  ]
}
```

### 问题 2: 时间类型异常

**错误**:
```
Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'
```

**原因**: Npgsql 时间类型兼容性问题

**解决方案**: ORM 已自动处理，无需额外配置

### 问题 3: 连接超时

**错误**:
```
Timeout during connection attempt
```

**原因**: 连接超时或网络问题

**解决方案**:
```json
"connectionString": "...;Timeout=60;..."
```

### 问题 4: SSL 连接问题

**错误**:
```
SSL connection error
```

**解决方案**:
```json
"connectionString": "...;SSL Mode=Disable;..."
```

或使用正确的 SSL 配置:
```json
"connectionString": "...;SSL Mode=Require;..."
```

---

## 🎯 性能优化建议

### 1. 连接池配置

**低并发场景** (< 100 QPS):
```
Minimum Pool Size=1;Maximum Pool Size=50;
```

**中并发场景** (100-1000 QPS):
```
Minimum Pool Size=5;Maximum Pool Size=100;
```

**高并发场景** (> 1000 QPS):
```
Minimum Pool Size=10;Maximum Pool Size=200;
```

### 2. 表名规范化

**推荐**: 保持默认 `autoToLower=true`

**优势**:
- ✅ 避免大小写问题
- ✅ 符合 PostgreSQL 规范
- ✅ 简化查询

### 3. 自增策略选择

**PostgreSQL 9.x**: 使用 `Serial`
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "identityStrategy": "Serial"
      }
    }
  ]
}
```

**PostgreSQL 10+**: 使用 `Identity`（推荐）
```json
{
  "databases": [
    {
      "name": "postgres-main",
      "optimizationSettings": {
        "identityStrategy": "Identity"
      }
    }
  ]
}
```

---

## 🌐 特殊类型支持

### JSON / JSONB 类型

```csharp
[SugarColumn(IsJson = true)]
public List<Order> JsonText { get; set; }

// 查询
var list = db.Queryable<User>()
    .Where(it => SqlFunc.JsonContainsFieldName(it.JsonText, "name"))
    .ToList();
```

### 数组类型

```csharp
[SugarColumn(ColumnDataType = "text[]", IsArray = true)]
public string[] MenuIds { get; set; }

// 查询（需要 SqlSugarCore 5.1.4.158-preview15+）
var list = db.Queryable<User>()
    .Where(it => SqlFunc.PgsqlArrayContains(it.MenuIds, "admin"))
    .ToList();
```

### Geometry / PostGIS 类型

**要求**: 安装 `Npgsql.NetTopologySuite`

**配置**:
```csharp
// 在连接后配置
db.Ado.OpenAlways();
((NpgsqlConnection)db.Ado.Connection).TypeMapper.UseNetTopologySuite();
```

**使用**:
```csharp
[SugarColumn(ColumnDataType = "geometry")]
public Point Location { get; set; }
```

---

## 📚 相关文档

- [性能优化指南](../Doc/performance-optimization.md)
- [连接字符串优化](../Doc/connection-string-optimization.md)
- [快速参考](../Doc/quick-reference.md)
- [PostgreSQL 官方文档](../Doc/donet5_postgresql.md)

---

**最后更新**: 2025-12-01
