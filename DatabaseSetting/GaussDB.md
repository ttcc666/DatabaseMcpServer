# 华为 GaussDB / OpenGauss 数据库配置指南

本文档详细说明华为 GaussDB 和 OpenGauss 数据库的配置方法和性能优化策略。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### Npgsql 方式（推荐 - 稳定）

```json
{
  "databases": [
    {
      "name": "gaussdb-npgsql",
      "connectionString": "PORT=5432;DATABASE=SqlSugar4xTest;HOST=localhost;PASSWORD=haosql;USER ID=postgres;No Reset On Close=true;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;",
      "dbType": "PostgreSQL",
      "description": "GaussDB Npgsql 兼容模式",
      "isDefault": true
    }
  ]
}
```

#### 原生驱动方式（推荐 - 特性完整）

```json
{
  "databases": [
    {
      "name": "gaussdb-native",
      "connectionString": "PORT=5432;DATABASE=SqlSugar4xTest;HOST=localhost;PASSWORD=haosql;USER ID=postgres;",
      "dbType": "GaussDBNative",
      "description": "GaussDB 原生驱动模式",
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
| `HOST` / `Server` | 服务器地址 | `localhost` | 可以是 IP 或域名 |
| `PORT` / `Port` | 端口号 | `5432` | GaussDB 默认端口 |
| `DATABASE` / `Database` | 数据库名 | `mydb` | 要连接的数据库 |
| `USER ID` / `Username` | 用户名 | `postgres` | 数据库用户 |
| `PASSWORD` / `Password` | 密码 | `haosql` | 用户密码 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Pooling` | 启用连接池 | `true` | 连接复用，减少 **95%** 连接开销 |
| `Minimum Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Maximum Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `No Reset On Close` | 关闭时不重置 | `true` | 提高连接复用效率 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |
| `Command Timeout` | 命令超时（秒） | `30` | 避免长查询阻塞 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `SSL Mode` | SSL 模式 | `Disable` | 安全连接：`Require` |
| `Trust Server Certificate` | 信任服务器证书 | `false` | SSL 连接时设为 `true` |
| `Encoding` | 字符编码 | `UTF8` | 字符集配置 |
| `Search Path` | Schema 搜索路径 | `public` | 多 Schema 场景 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 GaussDB/OpenGauss 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| 原生驱动支持 | 支持 GaussDB 原生驱动 | 访问 GaussDB 特有特性 |
| Npgsql 兼容 | 支持 Npgsql 驱动 | 成熟稳定，生态丰富 |
| 禁用 nvarchar | 自动禁用 nvarchar | 优化存储和性能 |
| Schema 管理 | 支持多 Schema | 多租户隔离 |
| 数据类型映射 | 支持 JSON、Geometry 等 | 现代应用开发 |
| 批量操作优化 | 批量插入优化 | 大数据导入性能提升 10-50 倍 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### nativeDriver

**说明**: 使用原生驱动

**类型**: `boolean`

**默认值**: `false`（使用 Npgsql）

**示例**:
```json
"optimizationSettings": {
  "nativeDriver": true
}
```

**效果**:
- `true`: 使用 GaussDB 原生驱动（访问特有特性）
- `false`: 使用 Npgsql 驱动（成熟稳定）

---

### isOpenGauss

**说明**: 标识为 OpenGauss 数据库

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "isOpenGauss": true
}
```

**效果**: 使用 OpenGauss 特定配置

---

### schema

**说明**: 指定 Schema 名称

**类型**: `string`

**默认值**: `public`

**示例**:
```json
"optimizationSettings": {
  "schema": "myschema"
}
```

**效果**: 多租户隔离，提高安全性

---

### typeMapping

**说明**: 启用数据类型映射优化

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "typeMapping": true
}
```

**效果**: 支持 JSON、Geometry 等特殊类型

---

### batchSize

**说明**: 批量操作大小

**类型**: `integer`

**默认值**: `1000`

**示例**:
```json
"optimizationSettings": {
  "batchSize": 5000
}
```

**效果**: 优化批量插入性能

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

### 生产环境配置（原生驱动）

```json
{
  "databases": [
    {
      "name": "gaussdb-prod",
      "connectionString": "PORT=5432;DATABASE=production;HOST=prod.gaussdb.com;PASSWORD=secure_password;USER ID=app_user;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=200;Connection Timeout=30;",
      "dbType": "GaussDBNative",
      "description": "GaussDB 生产环境（原生驱动）",
      "isDefault": true,
      "optimizationSettings": {
        "nativeDriver": true,
        "typeMapping": true,
        "batchSize": 5000,
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
- ✅ 原生驱动（特性完整）
- ✅ 大连接池（5-200）
- ✅ 类型映射优化
- ✅ 批量操作优化

---

### 开发环境配置（Npgsql）

```json
{
  "databases": [
    {
      "name": "gaussdb-dev",
      "connectionString": "PORT=5432;DATABASE=dev_db;HOST=localhost;PASSWORD=dev123;USER ID=postgres;No Reset On Close=true;",
      "dbType": "PostgreSQL",
      "description": "GaussDB 开发环境（Npgsql）",
      "isDefault": true,
      "optimizationSettings": {
        "nativeDriver": false
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
- ✅ Npgsql 驱动（稳定）
- ✅ 简化配置
- ✅ 快速开发

---

### OpenGauss 配置

```json
{
  "databases": [
    {
      "name": "opengauss-main",
      "connectionString": "PORT=5432;DATABASE=mydb;HOST=localhost;PASSWORD=pass;USER ID=postgres;",
      "dbType": "OpenGauss",
      "description": "OpenGauss 数据库",
      "isDefault": true,
      "optimizationSettings": {
        "isOpenGauss": true,
        "typeMapping": true
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
- ✅ OpenGauss 特定配置
- ✅ 类型映射优化

---

### 多 Schema 配置

```json
{
  "databases": [
    {
      "name": "gaussdb-multi-schema",
      "connectionString": "PORT=5432;DATABASE=mydb;HOST=localhost;PASSWORD=pass;USER ID=postgres;Search Path=tenant1,public;",
      "dbType": "GaussDBNative",
      "description": "GaussDB 多 Schema 配置",
      "isDefault": true,
      "optimizationSettings": {
        "schema": "tenant1"
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
- ✅ 多 Schema 支持
- ✅ 租户隔离

---

## 🔍 常见问题

### 问题 1: 选择 Npgsql 还是原生驱动？

**Npgsql 方式**:
- ✅ 优点: 成熟稳定，生态丰富
- ❌ 缺点: 无法使用 GaussDB 特有特性

**原生驱动方式**:
- ✅ 优点: 访问 GaussDB 特有特性
- ❌ 缺点: 需要安装 `SqlSugar.GaussDBNativeCore`

**建议**:
- 生产环境: 原生驱动（特性完整）
- 开发环境: Npgsql（快速开发）

---

### 问题 2: 连接超时

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

### 问题 3: SSL 连接问题

**错误**:
```
SSL connection error
```

**解决方案**:

**禁用 SSL**:
```json
"connectionString": "...;SSL Mode=Disable;..."
```

**启用 SSL**:
```json
"connectionString": "...;SSL Mode=Require;Trust Server Certificate=true;..."
```

---

### 问题 4: Schema 访问权限

**错误**:
```
Access denied for schema 'myschema'
```

**原因**: 用户没有 Schema 访问权限

**解决方案**:
```sql
-- 授予 Schema 权限
GRANT ALL PRIVILEGES ON SCHEMA myschema TO app_user;
```

---

### 问题 5: JSON 类型不支持

**错误**:
```
Type 'json' does not exist
```

**原因**: 未启用类型映射优化

**解决方案**:
在 databases.json 中添加优化配置：
```json
"optimizationSettings": {
  "typeMapping": true
}
```

---

## 🎯 性能优化建议

### 1. 驱动选择

| 场景 | 推荐驱动 | 原因 |
|------|---------|------|
| 生产环境 | 原生驱动 | 特性完整，性能最优 |
| 开发环境 | Npgsql | 快速开发，生态丰富 |
| 迁移项目 | Npgsql | 兼容性好，风险低 |

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

### 3. 批量操作优化

使用批量插入:
```json
"optimizationSettings": {
  "batchSize": 5000
}
```

性能对比:
- 普通插入: 1000 条/秒
- 批量插入: 10,000-50,000 条/秒

---

### 4. Schema 隔离

多租户场景使用 Schema 隔离:
```json
"optimizationSettings": {
  "schema": "tenant1"
}
```

优势:
- ✅ 数据隔离
- ✅ 权限管理
- ✅ 易于维护

---

### 5. 数据类型优化

启用类型映射:
```json
"optimizationSettings": {
  "typeMapping": true
}
```

支持类型:
- ✅ JSON
- ✅ JSONB
- ✅ Geometry
- ✅ Array

---

## 🔗 驱动安装

### Npgsql 方式

```bash
# 已包含在 SqlSugarCore 中
dotnet add package SqlSugarCore
```

### 原生驱动方式

```bash
# 需要额外安装
dotnet add package SqlSugarCore
dotnet add package SqlSugar.GaussDBNativeCore
```

---

## 📚 相关文档

- [华为 GaussDB/OpenGauss .NET 操作](../Doc/donet5_gaussdb.md)
- [扩展数据库优化策略](../Doc/extending-database-optimization.md)
- [主 README](../README.md)

---

## 💡 最佳实践

1. **驱动选择**: 生产环境使用原生驱动，开发环境使用 Npgsql
2. **连接池**: 根据并发量合理配置连接池大小
3. **Schema 隔离**: 多租户场景使用 Schema 进行隔离
4. **类型映射**: 启用类型映射以支持 JSON、Geometry 等类型
5. **批量操作**: 大数据导入使用批量插入
6. **SSL 配置**: 生产环境启用 SSL 加密连接
7. **驱动版本**: 使用最新版本驱动以获得最佳性能

---

**最后更新**: 2025-12-01
