# MySQL / MariaDB / TiDB 数据库配置指南

本文档详细说明 MySQL 系列数据库（MySQL、MariaDB、TiDB、PolarDB 等兼容 MySQL 协议产品）的配置方法，并给出常见性能/兼容性选项（含禁用 Nvarchar）。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

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
| `AllowLoadLocalInfile` | 原生 LOAD DATA LOCAL 选项 | 按需配置 | 不开启 MCP BulkCopy |
| `Pooling` | 启用连接池 | `true` | 驱动复用物理连接；效果需实测 |
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

## 驱动与优化配置

每次操作独占 SqlSugarClient，物理连接由 MySqlConnector 的连接池复用。字符集、池大小、TLS 和用户变量应使用连接字符串配置，optimizationSettings 不覆盖驱动选项。

| optimizationSettings 键 | 状态 | 作用 |
| --- | --- | --- |
| disableNvarchar | mapped，默认 false | 映射到 ConnMoreSettings.DisableNvarchar，作用于相应 ORM 生成路径，不改写原始 SQL |
| maxPoolSize / charset / enableSsl / allowUserVariables | advisory | 保留用于诊断；分别使用连接串 Max Pool Size、Charset、SslMode、Allow User Variables |
| enableBulkCopy | not-applicable | MCP 没有 BulkCopy 工具，开关不改变插入行为 |

单行整数自增插入使用 insert_row；多条 DML 使用 execute_command 或 batch_execute_commands，并检查逐项结果。MySQL 当前不支持 atomic=true，不能将命令批次视为事务。连接串 AllowLoadLocalInfile 不会开启 MCP BulkCopy。

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
      "isDefault": true
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
      "isDefault": true
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

optimizationSettings.charset 仅保留为 advisory，不会改变连接或服务端字符集。

同时确保数据库和表使用 `utf8mb4`:
```sql
ALTER DATABASE mydb CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci;
ALTER TABLE mytable CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
```

### 问题 2: enableBulkCopy 为什么没有效果

该键是 not-applicable，当前没有 BulkCopy 执行路径。单行自增插入使用 insert_row，其他 DML 使用命令工具。需要原生批量导入时，使用数据库管理工具并按其要求配置客户端与服务端。

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
      "connectionString": "...;SslMode=Required;..."
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

### 2. 批量操作

batch_execute_commands 逐条执行参数化命令；非原子批次的 commandTimeoutSeconds 覆盖整个批次，包含排队、执行和读取。失败时检查逐项结果，不能把已成功命令自动重放。此工具不使用 BulkCopy。

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

**最后更新**: 2026-11-26
