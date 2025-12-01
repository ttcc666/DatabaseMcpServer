# Oracle 数据库配置指南

本文档详细说明 Oracle 数据库的配置方法。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "oracle-main",
      "connectionString": "Data Source=localhost/orcl;User ID=system;Password=oracle123;Pooling=true;Min Pool Size=5;Max Pool Size=150;Connection Timeout=60;",
      "dbType": "Oracle",
      "description": "Oracle 主库（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "camelCase": "false",
        "enableIdentity": "true",
        "maxParamLength": "30"
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
| `Data Source` | 数据源 | `localhost/orcl` | 服务器/服务名 |
| `User ID` | 用户名 | `system` | 数据库用户 |
| `Password` | 密码 | `oracle123` | 用户密码 |

### 连接字符串格式

#### 格式 1: 简单格式（推荐）
```
Data Source=localhost/orcl;User ID=system;Password=oracle123
```

#### 格式 2: 完整描述符
```
Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=150.158.57.125)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=ORCL)));User Id=system;Password=oracle123
```

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Pooling` | 启用连接池 | `true` | **必需**，Oracle 连接开销大 |
| `Min Pool Size` | 最小连接数 | `5` | 保持 5 个连接，避免频繁创建 |
| `Max Pool Size` | 最大连接数 | `150` | Oracle 支持大连接池 |
| `Connection Timeout` | 连接超时（秒） | `60` | Oracle 连接较慢，建议 60 秒 |

**重要**: Oracle 连接建立开销大，连接池是**必需**的，否则性能极差。

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `Incr Pool Size` | 连接池增量 | `5` | 连接不足时增加数量 |
| `Decr Pool Size` | 连接池减量 | `1` | 连接空闲时减少数量 |
| `Statement Cache Size` | 语句缓存大小 | `0` | 提高重复查询性能 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 Oracle 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| 大连接池 | 自动配置 5-150 连接 | 避免频繁创建连接 |
| 表名大小写处理 | 智能转换 | 避免表名找不到 |
| 原生自增支持 | Oracle 12C+ | 简化自增列使用 |
| 参数名长度限制 | Oracle 11 | 避免参数名过长错误 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### camelCase

**说明**: 使用驼峰表名（不自动转大写）

**类型**: `boolean`

**默认值**: `false`（自动转大写）

**示例**:
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "camelCase": "false"
      }
    }
  ]
}
```

**效果**:
- `false`: 表名使用大写（Oracle 标准）
- `true`: 表名使用驼峰（现代开发习惯）

**使用场景**:
- 数据库表名使用驼峰命名（如 `UserInfo`）
- 避免 Oracle 自动转大写导致表名不匹配

**效果对比**:
```sql
-- camelCase=false（默认）
SELECT * FROM USERINFO  -- 自动转大写

-- camelCase=true
SELECT * FROM "UserInfo"  -- 保持驼峰
```

---

### enableIdentity

**说明**: 启用 Oracle 12C+ 原生自增列

**类型**: `boolean`

**默认值**: `false`（使用序列）

**示例**:
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "enableIdentity": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 使用 Oracle 12C+ 原生自增（推荐）
- `false`: 使用传统序列方式

**使用场景**:
- Oracle 12C 及以上版本
- 简化自增列使用，无需手动创建序列

**对比**:

**使用序列（Oracle 11 及以下）**:
```sql
-- 创建序列
CREATE SEQUENCE SEQ_ID MINVALUE 1 MAXVALUE 99999999 START WITH 1 INCREMENT BY 1;

-- 实体配置
[SugarColumn(IsPrimaryKey = true, OracleSequenceName = "SEQ_ID")]
public int ItemId { get; set; }
```

**使用 Identity（Oracle 12C+）**:
```sql
-- 无需创建序列，自动生成

-- 实体配置
[SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
public int ItemId { get; set; }
```

---

### maxParamLength

**说明**: 限制参数名最大长度

**类型**: `integer`

**默认值**: 无限制

**示例**:
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "maxParamLength": "30"
      }
    }
  ]
}
```

**效果**: 参数名超过指定长度时自动截断

**使用场景**:
- Oracle 11 及以下版本
- 避免参数名过长导致 ORA-01745 错误

**错误示例**:
```
ORA-01745: invalid host/bind variable name
```

**原因**: Oracle 11 参数名限制 30 字符

---

## 📝 完整配置示例

### 生产环境配置（Oracle 12C+）

```json
{
  "databases": [
    {
      "name": "oracle-prod",
      "connectionString": "Data Source=prod.oracle.com/orcl;User ID=app_user;Password=SecureP@ssw0rd;Pooling=true;Min Pool Size=10;Max Pool Size=200;Connection Timeout=60;Statement Cache Size=50;",
      "dbType": "Oracle",
      "description": "Oracle 生产环境（高并发配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableIdentity": "true"
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
- ✅ 大连接池（10-200）
- ✅ 语句缓存（50）
- ✅ Oracle 12C+ 原生自增
- ✅ 高并发支持

---

### 开发环境配置（Oracle 11）

```json
{
  "databases": [
    {
      "name": "oracle-dev",
      "connectionString": "Data Source=localhost/orcl;User ID=system;Password=oracle123;Pooling=true;Min Pool Size=5;Max Pool Size=50;",
      "dbType": "Oracle",
      "description": "Oracle 开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "maxParamLength": "30"
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
- ✅ 小连接池（5-50）
- ✅ Oracle 11 兼容
- ✅ 参数名长度限制

---

### 驼峰表名配置

```json
{
  "databases": [
    {
      "name": "oracle-camel",
      "connectionString": "Data Source=localhost/orcl;User ID=system;Password=oracle123;Pooling=true;Min Pool Size=5;Max Pool Size=100;",
      "dbType": "Oracle",
      "description": "Oracle 驼峰表名配置",
      "isDefault": true,
      "optimizationSettings": {
        "camelCase": "true"
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
ORA-00942: table or view does not exist
```

**原因**: Oracle 默认转大写，但表是驼峰命名

**解决方案**:
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "camelCase": "true"
      }
    }
  ]
}
```

---

### 问题 2: 参数名过长

**错误**:
```
ORA-01745: invalid host/bind variable name
```

**原因**: Oracle 11 参数名限制 30 字符

**解决方案**:
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "maxParamLength": "30"
      }
    }
  ]
}
```

---

### 问题 3: 查询慢 - 时间字段

**现象**: 时间字段查询慢，索引失效

**原因**: 参数类型与数据库字段不一致

**解决方案**:
```csharp
// 确保参数类型正确
var param = new SugarParameter("@date", DateTime.Now);
param.DbType = DbType.Date;  // 指定类型
```

### 问题 4: 字符串字段查询慢

**现象**: varchar2/nvarchar2 字段查询慢

**原因**: 参数类型不匹配

**解决方案**:
```csharp
// 方式 1: 特性指定
[SugarColumn(SqlParameterDbType = System.Data.DbType.AnsiString)]
public string Name { get; set; }

// 方式 2: 参数指定
var param = new SugarParameter("@name", "张三");
param.IsVarchar2 = true;
```

---

## 🎯 性能优化建议

### 1. 连接池配置（必需）

**重要**: Oracle 连接建立开销大，连接池是**必需**的。

**低并发场景** (< 50 QPS):
```
Min Pool Size=5;Max Pool Size=50;
```

**中并发场景** (50-500 QPS):
```
Min Pool Size=10;Max Pool Size=100;
```

**高并发场景** (> 500 QPS):
```
Min Pool Size=20;Max Pool Size=200;
```

### 2. 批量操作优化

**推荐**: 使用参数化分页插入

```csharp
db.Insertable(List<实体>).UseParameter().ExecuteCommand();
```

**性能对比**:
- 普通插入: 100 条/秒
- 参数化插入: 1,000 条/秒
- BulkCopy: 10,000 条/秒

### 3. 序列 vs Identity

**Oracle 11 及以下**: 使用序列
```sql
CREATE SEQUENCE SEQ_ID MINVALUE 1 MAXVALUE 99999999 START WITH 1 INCREMENT BY 1 NOCACHE ORDER;
```

**Oracle 12C+**: 使用 Identity（推荐）
```json
{
  "databases": [
    {
      "name": "oracle-main",
      "optimizationSettings": {
        "enableIdentity": "true"
      }
    }
  ]
}
```

### 4. 语句缓存

**启用语句缓存**: 提高重复查询性能

```
Statement Cache Size=50;
```

---

## 🌐 特殊类型支持

### Clob / Blob 类型

```csharp
// Clob
var param = new SugarParameter("@content", "大文本内容");
param.IsClob = true;

// Blob
var param = new SugarParameter("@file", new byte[] { });
param.CustomDbType = OracleDbType.Blob;
```

### 游标参数

```csharp
var param = new SugarParameter("@cursor", "");
param.IsRefCursor = true;
param.Direction = System.Data.ParameterDirection.Output;

db.Ado.ExecuteCommand("EXEC sp_name @cursor", param);
// param.Value 包含结果
```

---

## 📚 相关文档

- [性能优化指南](../Doc/performance-optimization.md)
- [连接字符串优化](../Doc/connection-string-optimization.md)
- [快速参考](../Doc/quick-reference.md)
- [Oracle 官方文档](../Doc/oracle.md)

---

**最后更新**: 2025-12-01
