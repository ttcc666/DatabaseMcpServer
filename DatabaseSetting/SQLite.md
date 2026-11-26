# SQLite 数据库配置指南

本文档详细说明 SQLite 数据库的配置方法，涵盖路径/加密/缓存模式、CodeFirst 增强及并发注意事项。

当前源码的工具与权限以 [工具手册](../TOOLS.md) 和 `get_database_capabilities` 为准。优化项状态见 [ORM 配置约定](../Doc/orm-configuration.md)；下文 C# 实体、CodeFirst 和原生驱动示例属于 SqlSugar 用法，不代表 MCP 提供对应参数或工具。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "sqlite-main",
      "connectionString": "Data Source=./data/myapp.db;Cache=Shared;Mode=ReadWriteCreate;",
      "dbType": "Sqlite",
      "description": "SQLite 主库（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableDefaultValue": "true",
        "enableDescription": "true",
        "enableDropColumn": "true"
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
| `Data Source` | 数据库文件路径 | `./data/myapp.db` | 相对或绝对路径 |

### 路径格式

#### 相对路径（推荐）
```
Data Source=./data/myapp.db
Data Source=myapp.db
```

#### 绝对路径
```
Data Source=C:\\database\\myapp.db
Data Source=/var/lib/myapp.db
```

#### 动态路径
```csharp
var dbPath = Path.Combine(Environment.CurrentDirectory, "myapp.db");
var connStr = $"Data Source={dbPath}";
```

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `Cache` | 缓存模式 | 按需选择 | Shared 改变缓存与锁行为，不保证性能提升 |
| `Mode` | 打开模式 | `ReadWriteCreate` | 自动创建数据库 |
| `Foreign Keys` | 外键约束 | `true` | 数据完整性 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `Password` | 数据库密码 | - | 加密数据库 |
| `Foreign Keys` | 启用外键约束 | `false` | 数据完整性：`true` |

---

## 驱动与优化配置

当前使用 Microsoft.Data.Sqlite。每次操作独占 SqlSugarClient，IsAutoCloseConnection=true；跨请求持久化应使用文件库。连接串由原生 builder 校验，不接受 Journal Mode 或 Synchronous 等旧驱动关键字。

| optimizationSettings 键 | 状态 | 作用 |
| --- | --- | --- |
| enableDefaultValue | mapped，默认 true | SqliteCodeFirstEnableDefaultValue |
| enableDescription | mapped，默认 true | SqliteCodeFirstEnableDescription |
| enableDropColumn | mapped，默认 false | SqliteCodeFirstEnableDropColumn |

以上三个选项仅用于 CodeFirst；MCP schema 工具使用 DbMaintenance，因此不能借这些开关开启默认值、备注或删列工具。以 get_database_capabilities 的具体操作能力为准。

query_table_page 需要非空主键；insert_row 需要 provider 可识别的单个整数自增列，当前 SqlSugar 对 SQLite 建表文本中的 AUTOINCREMENT 大小写有识别限制，见 [ORM 配置约定](../Doc/orm-configuration.md)。

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "sqlite-prod",
      "connectionString": "Data Source=./data/production.db;Cache=Shared;Mode=ReadWriteCreate;",
      "dbType": "Sqlite",
      "description": "SQLite 生产环境（性能优化配置）",
      "isDefault": true,
      "optimizationSettings": {
        "enableDefaultValue": "true",
        "enableDescription": "true"
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
- ✅ 共享缓存
- ✅ WAL 日志模式（性能优化）
- ✅ Normal 同步模式（性能优化）
- ✅ CodeFirst 增强

---

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "sqlite-dev",
      "connectionString": "Data Source=./dev.db;Cache=Shared;Mode=ReadWriteCreate;",
      "dbType": "Sqlite",
      "description": "SQLite 开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "enableDefaultValue": "true",
        "enableDescription": "true",
        "enableDropColumn": "true"
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
- ✅ 快速开发
- ✅ 支持删除列（.NET Core）

---

### 内存模式配置

```json
{
  "databases": [
    {
      "name": "sqlite-memory",
      "connectionString": "Data Source=:memory:;",
      "dbType": "Sqlite",
      "description": "SQLite 内存模式（临时数据）",
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

**注意**: 普通 :memory: 数据库只属于创建它的连接，关闭后数据丢失。本服务固定使用独立租约和 IsAutoCloseConnection=true，不能通过 JSON 配置关闭自动释放；跨工具调用请使用文件库。

---

### 加密模式配置

```json
{
  "databases": [
    {
      "name": "sqlite-encrypted",
      "connectionString": "Data Source=./secure.db;Password=YourSecurePassword;Cache=Shared;Mode=ReadWriteCreate;",
      "dbType": "Sqlite",
      "description": "SQLite 加密模式",
      "isDefault": true,
      "optimizationSettings": {
        "enableDefaultValue": "true",
        "enableDescription": "true"
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

**要求**: 安装 `SQLitePCLRaw.bundle_e_sqlcipher`

**边界**: Password 是否支持加密取决于实际 SQLite 原生库；仅添加连接串参数不能安装 SQLCipher。默认发布包不承诺加密能力，独立租约模型也不会切换为单例。

---

## 🔍 常见问题

### 问题 1: database is locked

**错误**:
```
database is locked
```

**原因**: 多个连接同时写入

**解决方案**:
```csharp
// 方式 1: 使用 using 语句
using (var db = new SqlSugarClient(...))
{
    // 操作数据库
}

// 方式 2: 手动释放
db.Dispose();
```

### 问题 2: 并发写入性能

**现象**: 并发写入慢或失败

**原因**: SQLite 单写入限制

**解决方案**:
```csharp
// 使用事务批量写入
db.Ado.UseTran(() =>
{
    db.Insertable(list).ExecuteCommand();
});

// 使用 CopyNew() 创建独立连接
await db.CopyNew().Updateable<Order>()
    .SetColumns(o => o.Name == "2")
    .Where(o => o.Id == 1)
    .ExecuteCommandAsync();

// 加密场景（打开慢，不推荐高并发）
lock(dbInstance)
{
    dbInstance.Insertable(data).ExecuteCommand();
}
```

### 问题 3: 文件占用无法删除

**错误**:
```
The process cannot access the file because it is being used by another process
```

**解决方案**:
```csharp
// .NET Core
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

// .NET Framework
System.Data.SQLite.SQLiteConnection.ClearAllPools();
```

### 问题 4: 查询不到时间

**原因**: 表中存在错误格式时间

**解决方案**: 定位格式异常的行，核对字段类型和日期格式，并在备份后定向修复数据。

---

## 🎯 性能优化建议

### 1. 缓存模式

**选择**: 按实际锁与缓存需求配置，不默认将 Shared 视为更快。

```
Cache=Shared;
```

**边界**: Shared 改变缓存与锁语义，需结合并发和日志模式验证，不承诺固定提升比例。

### 2. 日志模式

**推荐**: 使用 WAL 模式

```sql
PRAGMA journal_mode=WAL;
```

这是数据库 SQL，不是连接串关键字；请通过数据库管理工具在目标文件上配置并验证。

**优势**:
- ✅ 读写并发性能提升
- ✅ 减少锁等待
- ✅ 提高吞吐量

### 3. 同步模式

**推荐**: 使用 Normal 模式

```sql
PRAGMA synchronous=NORMAL;
```

这是连接级 SQL 设置；独立 MCP 调用使用不同租约，不能假定一次设置会作用于后续请求。

**性能对比**:
- `Full`: 最安全，最慢
- `Normal`: 平衡（推荐）
- `Off`: 最快，风险高

### 4. 批量操作

**推荐**: 使用事务批量写入

```csharp
db.Ado.UseTran(() =>
{
    db.Insertable(list).ExecuteCommand();
});
```

MCP 可用 batch_execute_commands 的 atomic=true 执行受支持的 DML 事务；根据实际锁竞争、磁盘和事务大小测量性能。

---

## 🌐 特殊模式

### 内存模式

**连接字符串**:
```
Data Source=:memory:;
```

**用途**:
- 临时数据存储
- 单元测试
- 缓存

**注意事项**:
```csharp
var db = new SqlSugarClient(new ConnectionConfig()
{
    IsAutoCloseConnection = false,  // 必须设置为 false
    DbType = DbType.Sqlite,
    ConnectionString = "Data Source=:memory:",
});

db.CodeFirst.InitTables<User>();
// 使用数据库...
// 连接关闭后数据丢失
```

### 加密模式

**连接字符串**:
```
Data Source=./secure.db;Password=YourPassword;
```

**要求**: 安装 `SQLitePCLRaw.bundle_e_sqlcipher`

**性能建议**:
```csharp
// 使用单例模式
public static SqlSugarClient GetInstance()
{
    if (_instance == null)
    {
        lock (_lock)
        {
            if (_instance == null)
            {
                _instance = new SqlSugarClient(...);
            }
        }
    }
    return _instance;
}
```
> 此为应用直接使用 SqlSugar 的示例；MCP 固定采用独立租约，不能通过配置切换为共享客户端或关闭自动释放。加密仍需匹配的原生库。

### 备份数据库

```csharp
// 解除文件占用
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

// 使用内置备份方法
db.DbMaintenance.BackupDataBase(null, "backup.db");
```

---

**最后更新**: 2026-11-26
