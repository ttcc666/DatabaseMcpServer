# 人大金仓数据库配置指南

本文档详细说明人大金仓数据库（Kingbase / Kdbndp）的配置方法和性能优化策略。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "kdbndp-main",
      "connectionString": "Server=127.0.0.1;Port=54321;UID=SYSTEM;PWD=system;database=SQLSUGAR4XTEST1;",
      "dbType": "Kdbndp",
      "description": "人大金仓数据库主库",
      "isDefault": true,
      "optimizationSettings": {
        "mode": "Oracle",
        "enableJson": true
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
| `Server` / `Host` | 服务器地址 | `127.0.0.1` | 可以是 IP 或域名 |
| `Port` | 端口号 | `54321` | 人大金仓默认端口 |
| `UID` / `User Id` | 用户名 | `SYSTEM` | 数据库用户 |
| `PWD` / `Password` | 密码 | `system` | 用户密码 |
| `database` / `Database` | 数据库名 | `mydb` | 要连接的数据库 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Pooling` | 启用连接池 | `true` | 连接复用，减少 **95%** 连接开销 |
| `Minimum Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Maximum Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |
| `Command Timeout` | 命令超时（秒） | `30` | 避免长查询阻塞 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `SSL Mode` | SSL 模式 | `Disable` | 安全连接：`Require` |
| `Trust Server Certificate` | 信任服务器证书 | `false` | SSL 连接时设为 `true` |
| `Encoding` | 字符编码 | `UTF8` | 字符集配置 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为人大金仓应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| 多模式兼容 | 支持 Oracle/MySQL/PostgreSQL/SqlServer 模式 | 灵活适配不同应用场景 |
| 智能表名处理 | 根据模式自动转换大小写 | 兼容不同命名规范 |
| 游标支持 | 支持游标参数 | 复杂查询性能优化 |
| JSON 类型 | 支持 JSON 数据类型 | 现代应用开发 |
| Geometry 支持 | 支持 Geometry/Postgis | 地理信息系统 |
| 数组类型 | 支持数组类型 | 复杂数据结构 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |

---

## 🔧 优化配置选项

以下配置选项在 `databases.json` 的 `optimizationSettings` 中设置。

### mode

**说明**: 数据库兼容模式

**类型**: `string`

**可选值**: `Oracle` / `MySQL` / `PostgreSQL` / `SqlServer`

**默认值**: `Oracle`

**示例**:
```json
"optimizationSettings": {
  "mode": "Oracle"
}
```

**效果**:
- `Oracle`: Oracle 兼容模式（默认，表名转大写）
- `MySQL`: MySQL 兼容模式
- `PostgreSQL`: PostgreSQL 兼容模式
- `SqlServer`: SQL Server 兼容模式

---

### camelCase

**说明**: 使用驼峰表名

**类型**: `boolean`

**默认值**: `false`（使用大写）

**示例**:
```json
"optimizationSettings": {
  "camelCase": true
}
```

**效果**:
- `false`: 表名自动转大写（Oracle 模式默认）
- `true`: 使用驼峰表名

---

### enableCursor

**说明**: 启用游标参数支持

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "enableCursor": true
}
```

**效果**: 支持存储过程中的游标参数

---

### enableJson

**说明**: 启用 JSON 类型支持

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "enableJson": true
}
```

**效果**: 支持 JSON 数据类型的存储和查询

---

### enableGeometry

**说明**: 启用 Geometry/Postgis 支持

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "enableGeometry": true
}
```

**效果**: 支持地理信息系统（GIS）数据类型

---

### enableArray

**说明**: 启用数组类型支持

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "enableArray": true
}
```

**效果**: 支持数组数据类型

---

### schema

**说明**: 指定 Schema 名称

**类型**: `string`

**默认值**: 无

**示例**:
```json
"optimizationSettings": {
  "schema": "myschema"
}
```

**效果**: 多租户隔离，提高安全性

---

### maxPoolSize

**说明**: 连接池最大连接数

**类型**: `integer`

**默认值**: `100`

**示例**:
```json
"optimizationSettings": {
  "maxPoolSize": 200
}
```

**效果**: 高并发场景下提高性能

---

## 📝 完整配置示例

### Oracle 兼容模式（生产环境）

```json
{
  "databases": [
    {
      "name": "kdbndp-oracle-prod",
      "connectionString": "Server=prod.kingbase.com;Port=54321;UID=APP_USER;PWD=secure_password;database=PRODUCTION;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=200;Connection Timeout=30;",
      "dbType": "Kdbndp",
      "description": "人大金仓 Oracle 模式生产环境",
      "isDefault": true,
      "optimizationSettings": {
        "mode": "Oracle",
        "enableCursor": true,
        "enableJson": true,
        "maxPoolSize": 200
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
- ✅ Oracle 兼容模式
- ✅ 大连接池（5-200）
- ✅ 游标支持
- ✅ JSON 类型支持

---

### PostgreSQL 兼容模式（开发环境）

```json
{
  "databases": [
    {
      "name": "kdbndp-pg-dev",
      "connectionString": "Server=localhost;Port=54321;UID=SYSTEM;PWD=system;database=DEV_DB;",
      "dbType": "Kdbndp",
      "description": "人大金仓 PostgreSQL 模式开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "mode": "PostgreSQL",
        "camelCase": true,
        "enableJson": true,
        "enableArray": true
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
- ✅ PostgreSQL 兼容模式
- ✅ 驼峰表名
- ✅ JSON 和数组类型支持

---

### MySQL 兼容模式（测试环境）

```json
{
  "databases": [
    {
      "name": "kdbndp-mysql-test",
      "connectionString": "Server=localhost;Port=54321;UID=test_user;PWD=test123;database=TEST_DB;",
      "dbType": "Kdbndp",
      "description": "人大金仓 MySQL 模式测试环境",
      "isDefault": true,
      "optimizationSettings": {
        "mode": "MySQL"
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
- ✅ MySQL 兼容模式
- ✅ 简化配置
- ✅ 快速测试

---

### GIS 应用配置

```json
{
  "databases": [
    {
      "name": "kdbndp-gis",
      "connectionString": "Server=localhost;Port=54321;UID=gis_user;PWD=gis123;database=GIS_DB;",
      "dbType": "Kdbndp",
      "description": "人大金仓 GIS 应用",
      "isDefault": true,
      "optimizationSettings": {
        "mode": "PostgreSQL",
        "enableGeometry": true,
        "enableJson": true
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
- ✅ Geometry/Postgis 支持
- ✅ JSON 类型支持
- ✅ 地理信息系统优化

---

## 🔍 常见问题

### 问题 1: 如何查看数据库模式

**查询命令**:
```sql
-- 查看数据库模式
show database_mode;

-- 或
SELECT version();
```

**输出示例**:
```
database_mode
--------------
Oracle
```

---

### 问题 2: 表名大小写问题

**错误**:
```
Table 'user' doesn't exist
```

**原因**: 不同模式下表名大小写规则不同

**解决方案**:

**Oracle 模式**: 使用大写表名
```json
"optimizationSettings": {
  "mode": "Oracle"
}
```

**PostgreSQL 模式**: 使用小写表名
```json
"optimizationSettings": {
  "mode": "PostgreSQL",
  "camelCase": true
}
```

---

### 问题 3: 游标参数不支持

**错误**:
```
Cursor parameter not supported
```

**原因**: 未启用游标支持

**解决方案**:
```json
"optimizationSettings": {
  "enableCursor": true
}
```

---

### 问题 4: JSON 类型错误

**错误**:
```
Type 'json' does not exist
```

**原因**: 未启用 JSON 类型支持

**解决方案**:
```json
"optimizationSettings": {
  "enableJson": true
}
```

---

### 问题 5: 连接超时

**错误**:
```
Connection timeout
```

**原因**: 网络延迟或服务器负载高

**解决方案**:
```json
"connectionString": "...;Connection Timeout=60;Command Timeout=60;..."
```

---

## 🎯 性能优化建议

### 1. 选择合适的兼容模式

| 应用场景 | 推荐模式 | 原因 |
|---------|---------|------|
| 从 Oracle 迁移 | `Oracle` | 最小化迁移成本 |
| 从 MySQL 迁移 | `MySQL` | 兼容 MySQL 语法 |
| 从 PostgreSQL 迁移 | `PostgreSQL` | 兼容 PostgreSQL 特性 |
| 新项目 | `PostgreSQL` | 功能最丰富 |

---

### 2. 连接池配置

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

---

### 3. 特殊类型使用

**JSON 类型**:
```json
"optimizationSettings": {
  "enableJson": true
}
```

**数组类型**:
```json
"optimizationSettings": {
  "enableArray": true
}
```

**Geometry 类型**:
```json
"optimizationSettings": {
  "enableGeometry": true
}
```

---

### 4. 表名规范

**Oracle 模式**: 统一使用大写
```sql
CREATE TABLE USER_INFO (
    ID INT PRIMARY KEY,
    NAME VARCHAR(100)
);
```

**PostgreSQL 模式**: 统一使用小写
```sql
CREATE TABLE user_info (
    id INT PRIMARY KEY,
    name VARCHAR(100)
);
```

---

## 🔗 版本兼容性

### R6 版本（推荐）

```json
"connectionString": "Server=127.0.0.1;Port=54321;UID=SYSTEM;PWD=system;database=mydb;"
```

### R3 版本

```json
"connectionString": "Server=127.0.0.1;Port=54321;UID=SYSTEM;PWD=system;database=mydb;"
```

**注意**: R6 和 R3 版本连接字符串格式相同，但功能支持有差异。

---

## 📚 相关文档

- [人大金仓 .NET 操作](../Doc/donet5_kdbndp.md)
- [扩展数据库优化策略](../Doc/extending-database-optimization.md)
- [性能优化指南](../Doc/performance-optimization.md)
- [主 README](../README.md)

---

## 💡 最佳实践

1. **模式选择**: 根据迁移源选择合适的兼容模式
2. **表名规范**: 统一使用大写或小写，避免混用
3. **连接池**: 根据并发量合理配置连接池大小
4. **特殊类型**: 按需启用 JSON、数组、Geometry 等类型支持
5. **游标支持**: 存储过程使用游标时启用游标支持
6. **Schema 隔离**: 生产环境使用 Schema 进行多租户隔离
7. **驱动版本**: 使用最新版本驱动以获得最佳性能

---

**最后更新**: 2025-12-01
