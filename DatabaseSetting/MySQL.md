# MySQL / MariaDB / TiDB 数据库配置指南

本文档详细说明 MySQL 系列数据库（MySQL、MariaDB、TiDB、PolarDB 等兼容 MySQL 协议产品）的配置方法，并给出常见性能/兼容性选项（含禁用 Nvarchar）。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "Server=localhost;Port=3306;Database=myapp;User=root;Password=123456;Charset=utf8mb4;AllowLoadLocalInfile=true;Min Pool Size=1;Max Pool Size=100;Allow User Variables=True;Pooling=true;Connection Timeout=30;",
      "dbType": "MySql",
      "description": "MySQL 主库（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableBulkCopy": "true",
        "maxPoolSize": "100",
        "disableNvarchar": "false"
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
| `Server` | 服务器地址 | `localhost` | 可以是 IP 或域名 |
| `Port` | 端口号 | `3306` | MySQL 默认端口 |
| `Database` | 数据库名 | `myapp` | 要连接的数据库 |
| `User` / `Uid` | 用户名 | `root` | 数据库用户 |
| `Password` / `Pwd` | 密码 | `123456` | 用户密码 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Charset` | 字符集 | `utf8mb4` | 支持完整 Unicode（包括 emoji） |
| `AllowLoadLocalInfile` | 启用批量导入 | `true` | 批量导入性能提升 **10-50 倍** |
| `Pooling` | 启用连接池 | `true` | 连接复用，减少 **95%** 连接开销 |
| `Min Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Max Pool Size` | 最大连接数 | `100` | 防止连接耗尽 |
| `Allow User Variables` | 支持用户变量 | `True` | 复杂查询性能优化 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `SslMode` | SSL 模式 | `None` | 安全连接：`Required` |
| `AllowPublicKeyRetrieval` | 允许公钥检索 | `false` | SSL 连接时设为 `true` |
| `Convert Zero Datetime` | 转换零日期 | `false` | 处理 `0000-00-00` 日期 |
| `Allow Zero Datetime` | 允许零日期 | `false` | 兼容旧数据 |
| `TreatTinyAsBoolean` | tinyint(1) 作为布尔 | `true` | 设为 `false` 保持数值 |
| `AllowLoadLocalInfile` | BulkCopy 需要 | `false` | 批量导入开启 |
| `Allow User Variables` | 用户变量 | `false` | 需要用户变量时开启 |
| `Pooling` | 连接池开关 | `true` | 特殊服务器不兼容时可设 `false` |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 MySQL 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |
| 字符集支持 | 自动识别 utf8mb4 | 避免乱码，支持 emoji |
| 批量操作 | 支持 BulkCopy | 大数据导入性能提升 10-50 倍 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### enableBulkCopy

**说明**: 启用批量导入优化

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "enableBulkCopy": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 启用 BulkCopy，大数据导入性能提升 10-50 倍
- `false`: 使用普通插入

**注意**: 需要在连接字符串中添加 `AllowLoadLocalInfile=true`，并在服务器执行 `SET GLOBAL local_infile=1`

---

### disableNvarchar

**说明**: 某些改造版 MySQL / 兼容库不支持 `N''` 语法，可选择禁用 Nvarchar 写入。

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "disableNvarchar": "true"
      }
    }
  ]
}
```

**效果**:
- `true`: 禁用 `N''` 前缀，避免特殊服务器报错
- `false`: 保持默认写入

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
      "name": "mysql-main",
      "optimizationSettings": {
        "maxPoolSize": "200"
      }
    }
  ]
}
```

**效果**: 高并发场景下提高性能

**推荐值**:
- 低并发 (< 100 QPS): `50`
- 中并发 (100-1000 QPS): `100`
- 高并发 (> 1000 QPS): `200`

---

### charset

**说明**: 字符集配置

**类型**: `string`

**默认值**: `utf8mb4`

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "charset": "utf8mb4"
      }
    }
  ]
}
```

**效果**: 支持完整 Unicode（包括 emoji）

**可选值**:
- `utf8mb4`: 完整 Unicode 支持（推荐）
- `utf8`: 基本 Unicode 支持
- `latin1`: 西欧字符集

---

### enableSsl

**说明**: 启用 SSL 安全连接

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "enableSsl": "true"
      }
    }
  ]
}
```

**效果**: 加密数据传输，提高安全性

**注意**: 需要在连接字符串中添加 `SslMode=Required`

---

### allowUserVariables

**说明**: 允许用户变量

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "allowUserVariables": "true"
      }
    }
  ]
}
```

**效果**: 支持复杂查询和存储过程中的用户变量

**注意**: 需要在连接字符串中添加 `Allow User Variables=True`

---

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "mysql-prod",
      "connectionString": "Server=prod.mysql.com;Port=3306;Database=production;User=app_user;Password=secure_password;Charset=utf8mb4;AllowLoadLocalInfile=true;Min Pool Size=5;Max Pool Size=200;Allow User Variables=True;Pooling=true;Connection Timeout=30;SslMode=Required;",
      "dbType": "MySql",
      "description": "MySQL 生产环境（高并发配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableBulkCopy": "true",
        "maxPoolSize": "200",
        "charset": "utf8mb4",
        "enableSsl": "true",
        "allowUserVariables": "true",
        "disableNvarchar": "false"
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
- ✅ 完整 Unicode 支持
- ✅ 批量导入支持

---

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "mysql-dev",
      "connectionString": "Server=localhost;Port=3306;Database=dev_db;User=root;Password=123456;Charset=utf8mb4;Min Pool Size=1;Max Pool Size=50;Pooling=true;",
      "dbType": "MySql",
      "description": "MySQL 开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "maxPoolSize": "50",
        "charset": "utf8mb4"
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

### 测试环境配置

```json
{
  "databases": [
    {
      "name": "mysql-test",
      "connectionString": "Server=localhost;Port=3306;Database=test_db;User=test_user;Password=test123;Charset=utf8mb4;Pooling=true;",
      "dbType": "MySql",
      "description": "MySQL 测试环境",
      "isDefault": true,
      "optimizationSettings": {
        "charset": "utf8mb4"
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

### 问题 1: emoji 存储乱码

**错误**:
```
Incorrect string value: '\xF0\x9F\x98\x80' for column 'content'
```

**原因**: 字符集不支持 emoji

**解决方案**:

**方案 1**: 在连接字符串中配置
```json
"connectionString": "...;Charset=utf8mb4;..."
```

**方案 2**: 在优化配置中设置
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "charset": "utf8mb4"
      }
    }
  ]
}
```

同时确保数据库和表使用 `utf8mb4`:
```sql
ALTER DATABASE mydb CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci;
ALTER TABLE mytable CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
```

### 问题 2: BulkCopy 失败

**错误**:
```
The used command is not allowed with this MySQL version
```

**原因**: 未启用 `AllowLoadLocalInfile`

**解决方案**:

**步骤 1**: 在连接字符串中添加
```json
"connectionString": "...;AllowLoadLocalInfile=true;..."
```

**步骤 2**: 在优化配置中启用
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "optimizationSettings": {
        "enableBulkCopy": "true"
      }
    }
  ]
}
```

**步骤 3**: 在服务器执行
```sql
SET GLOBAL local_infile=1;
```

### 问题 3: 连接超时

**错误**:
```
Unable to connect to any of the specified MySQL hosts
```

**原因**: 连接超时或网络问题

**解决方案**:
```json
"connectionString": "...;Connection Timeout=60;..."
```

### 问题 4: SSL 连接问题

**错误**:
```
SSL connection error
```

**解决方案**:

**方案 1**: 禁用 SSL（开发环境）
```json
"connectionString": "...;SslMode=none;AllowPublicKeyRetrieval=True;..."
```

**方案 2**: 启用 SSL（生产环境）
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "...;SslMode=Required;...",
      "optimizationSettings": {
        "enableSsl": "true"
      }
    }
  ]
}
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

### 2. 批量操作优化

使用 BulkCopy 进行大数据导入:
```csharp
// 确保连接字符串包含
AllowLoadLocalInfile=true
```

性能对比:
- 普通插入: 1000 条/秒
- BulkCopy: 10,000-50,000 条/秒

### 3. 查询优化

避免大数据量联表查询:
- ✅ 使用导航查询 `Includes`（拆分算法，减少联表 Count/分页开销）
- ✅ 分页查询
- ❌ 避免 `SELECT *`
- ❌ 避免大表联表 Count

---

## 🌐 兼容数据库

以下数据库使用相同的配置方式：

| 数据库 | DbType | 说明 |
|--------|--------|------|
| MySQL | `MySql` | 官方 MySQL |
| MariaDB | `MySql` | MySQL 分支 |
| TiDB | `tidb` | 分布式数据库 |
| PolarDB | `polardb` | 阿里云数据库 |
| Percona / Aurora / Azure MySQL / GCP MySQL | `MySql` | 云上 MySQL 兼容 |
| 部分定制 MySQL (需禁用 Nvarchar 时) | `MySql` | 配合 `disableNvarchar` |

---

**最后更新**: 2025-12-10
