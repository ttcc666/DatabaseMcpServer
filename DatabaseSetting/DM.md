# 达梦数据库配置指南

本文档详细说明达梦数据库（DM Database）的配置方法和性能优化策略，涵盖表名大小写、Schema、Docker/MySQL 模式兼容、Clob 优化及驱动/版本提示。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "dm-main",
      "connectionString": "Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA;DATABASE=DAMENG;SCHEMA=myschema;",
      "dbType": "Dm",
      "description": "达梦数据库主库",
      "isDefault": true,
      "optimizationSettings": {
        "lowercaseTables": "false",
        "clobOptimization": "true",
        "dockerMysqlMode": "false"
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
| `Server` | 服务器地址和端口 | `localhost:5236` | 格式: `host:port` |
| `User Id` / `UID` | 用户名 | `SYSDBA` | 数据库用户 |
| `PWD` / `Password` | 密码 | `SYSDBA` | 用户密码 |
| `DATABASE` | 数据库名 | `DAMENG` | 要连接的数据库 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `SCHEMA` | Schema 名称 | `myschema` | 多租户隔离 |
| `Pooling` | 启用连接池 | `true` | 连接复用，减少 **95%** 连接开销 |
| `Min Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |
| `DatabaseModel` | Docker/MySQL 兼容 | `MySql` | Docker 误装 MySQL 模式时 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `PORT` | 端口号（旧版） | `5236` | 旧版驱动使用 |
| `HOST` | 主机地址（旧版） | `localhost` | 旧版驱动使用 |
| `Encrypt` | 启用加密 | `false` | 安全连接 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为达梦数据库应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| 智能表名处理 | 默认转大写，可配置小写 | 兼容不同命名规范 |
| Docker 模式兼容 | 自动识别 MySQL 兼容模式 | 解决 Docker 部署问题 |
| Clob 类型优化 | 大文本字段优化 | 避免插入空白问题 |
| Schema 支持 | 多租户隔离 | 提高安全性和组织性 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |
| 连接串兼容提示 | 支持新旧格式 | 降低迁移成本 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### lowercaseTables

**说明**: 使用小写表名

**类型**: `boolean`

**默认值**: `false`（使用大写）

**示例**:
```json
{
  "databases": [
    {
      "name": "dm-main",
      "optimizationSettings": {
        "lowercaseTables": "false"
      }
    }
  ]
}
```

**效果**:
- `false`: 表名自动转大写（默认）
- `true`: 使用小写表名

---

### dockerMysqlMode

**说明**: Docker MySQL 兼容模式

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "dm-main",
      "optimizationSettings": {
        "dockerMysqlMode": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 启用 MySQL 兼容模式（解决 Docker 部署分页等问题）
- `false`: 使用标准达梦模式

**提示**: 对应 `ConnMoreSettings.DatabaseModel = DbType.MySql`，需 SqlSugarCore 5.1.4.157-preview09+。

---

### schema

**说明**: 指定 Schema 名称

**类型**: `string`

**默认值**: 无

**示例**:
```json
{
  "databases": [
    {
      "name": "dm-main",
      "optimizationSettings": {
        "schema": "myschema"
      }
    }
  ]
}
```

**效果**: 多租户隔离，提高安全性

---

### clobOptimization

**说明**: 启用 Clob 类型优化

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "dm-main",
      "optimizationSettings": {
        "clobOptimization": "true"
      }
    }
  ]
}
```

**效果**: 解决大文本字段插入空白问题（需要 SqlSugarCore.Dm 1.3.0+）

---

### maxPoolSize

**说明**: 连接池最大连接数

**类型**: `integer`

**默认值**: `100`

**示例**:
```json
{
  "databases": [
    {
      "name": "dm-main",
      "optimizationSettings": {
        "maxPoolSize": "200"
      }
    }
  ]
}
```

**效果**: 高并发场景下提高性能

---

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "dm-prod",
      "connectionString": "Server=prod.dm.com:5236;User Id=APP_USER;PWD=secure_password;DATABASE=PRODUCTION;SCHEMA=prod_schema;Pooling=true;Min Pool Size=5;Max Pool Size=200;Connection Timeout=30;",
      "dbType": "Dm",
      "description": "达梦数据库生产环境（高并发配置）",
      "isDefault": true,
      "optimizationSettings": {
        "clobOptimization": "true",
        "maxPoolSize": "200",
        "schema": "prod_schema",
        "dockerMysqlMode": "false"
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
- ✅ Schema 隔离
- ✅ Clob 优化
- ✅ 生产级安全配置

---

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "dm-dev",
      "connectionString": "Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA;DATABASE=DEV_DB;",
      "dbType": "Dm",
      "description": "达梦数据库开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "lowercaseTables": "true"
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
- ✅ 简化配置
- ✅ 小写表名（可选）
- ✅ 快速开发

---

### Docker 环境配置

```json
{
  "databases": [
    {
      "name": "dm-docker",
      "connectionString": "Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA;DATABASE=DAMENG;",
      "dbType": "Dm",
      "description": "达梦数据库 Docker 环境",
      "isDefault": true,
      "optimizationSettings": {
        "dockerMysqlMode": "true"
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
- ✅ 解决 Docker 部署问题
- ✅ 分页等功能正常工作

---

## 🔍 常见问题

### 问题 1: 表名大小写问题

**错误**:
```
Table 'user' doesn't exist
```

**原因**: 达梦数据库默认使用大写表名

**解决方案**:

**方案 1**: 使用大写表名（推荐）
```json
{
  "optimizationSettings": {
    "lowercaseTables": "false"
  }
}
```

**方案 2**: 配置使用小写表名
```json
{
  "optimizationSettings": {
    "lowercaseTables": "true"
  }
}
```

---

### 问题 2: Clob 字段插入空白

**错误**: Clob 字段插入后显示为空白

**原因**: 驱动版本过旧

**解决方案**:
1. 升级到 `SqlSugarCore.Dm 1.3.0+`
2. 启用 Clob 优化:
```json
{
  "optimizationSettings": {
    "clobOptimization": "true"
  }
}
```

---

### 问题 3: Docker 部署分页问题

**错误**: 分页查询返回错误结果

**原因**: Docker 安装默认使用 MySQL 模式

**解决方案**:
```json
{
  "optimizationSettings": {
    "dockerMysqlMode": "true"
  }
}
```

---

### 问题 4: 连接超时

**错误**:
```
Connection timeout
```

**原因**: 网络延迟或服务器负载高

**解决方案**:
```json
"connectionString": "...;Connection Timeout=60;..."
```

---

### 问题 5: Schema 访问权限

**错误**:
```
Access denied for schema 'myschema'
```

**原因**: 用户没有 Schema 访问权限

**解决方案**:
```sql
-- 授予 Schema 权限
GRANT ALL PRIVILEGES ON SCHEMA myschema TO APP_USER;
```

---

## 🎯 性能优化建议

### 1. 连接池配置

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

---

### 2. 表名规范

**推荐**: 使用大写表名（达梦默认）
```sql
CREATE TABLE USER_INFO (
    ID INT PRIMARY KEY,
    NAME VARCHAR(100)
);
```

**或**: 统一使用小写表名
```json
{
  "optimizationSettings": {
    "lowercaseTables": "true"
  }
}
```

---

### 3. Clob 字段优化

使用 Clob 字段时:
1. 确保驱动版本 >= 1.3.0
2. 启用 Clob 优化
3. 使用 `NClobPropertyConvert` 特性

```csharp
[SugarColumn(SqlParameterDbType = typeof(NClobPropertyConvert))]
public string Content { get; set; }
```

---

### 4. Schema 隔离

多租户场景使用 Schema 隔离:
```json
"connectionString": "...;SCHEMA=tenant1;..."
```

优势:
- ✅ 数据隔离
- ✅ 权限管理
- ✅ 易于维护

---

## 🔗 连接字符串版本对比

### 旧版本格式

```
PORT=5236;DATABASE=DAMENG;HOST=localhost;PASSWORD=SYSDBA;USER ID=SYSDBA
```

### 新版本格式（推荐）

```
Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA;DATABASE=DAMENG
```

### 带 Schema 格式

```
Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA;SCHEMA=myschema;DATABASE=DAMENG
```

---

## 📚 相关文档

- [达梦数据库 .NET 操作](../Doc/donet5_dm.md)
- [扩展数据库优化策略](../Doc/extending-database-optimization.md)
- [主 README](../README.md)

---

## 💡 最佳实践

1. **表名规范**: 统一使用大写或小写，避免混用
2. **Schema 隔离**: 生产环境使用 Schema 进行多租户隔离
3. **连接池**: 根据并发量合理配置连接池大小
4. **Clob 优化**: 大文本字段启用 Clob 优化
5. **Docker 部署**: 注意 MySQL 兼容模式配置
6. **驱动版本**: 使用最新版本驱动（SqlSugarCore.Dm 1.3.0+）
7. **Schema 创建**: 使用 `db.DbMaintenance.CreateDatabase()` 仅创建 Schema，数据库需已存在（5.1.4.199-preview30+）
8. **varchar(36) 转 GUID**: 若需禁用，连接字符串配置 `varchar36ToGuid=false`

---

**最后更新**: 2025-12-10
