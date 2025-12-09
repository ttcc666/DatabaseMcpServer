# SQLite 数据库配置指南

本文档详细说明 SQLite 数据库的配置方法，涵盖路径/加密/缓存模式、CodeFirst 增强及并发注意事项。

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
| `Cache` | 缓存模式 | `Shared` | 多连接性能提升 **30-50%** |
| `Mode` | 打开模式 | `ReadWriteCreate` | 自动创建数据库 |
| `Journal Mode` | 日志模式 | `WAL` | 读写并发性能提升 |
| `Synchronous` | 同步模式 | `Normal` | 性能/安全平衡 |
| `Foreign Keys` | 外键约束 | `true` | 数据完整性 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `Password` | 数据库密码 | - | 加密数据库 |
| `Foreign Keys` | 启用外键约束 | `false` | 数据完整性：`true` |
| `Journal Mode` | 日志模式 | `Delete` | 性能优化：`WAL` |
| `Synchronous` | 同步模式 | `Full` | 性能优化：`Normal` |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 SQLite 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| **CodeFirst 默认值** | 支持字段默认值 | 开发效率提升 |
| **CodeFirst 备注** | 支持字段备注 | 文档化提升 |
| **CodeFirst 删除列** | 支持删除列（.NET Core） | DDL 完整性 |
| 共享缓存 | 多连接共享缓存 | 性能提升 30-50% |
| 可选 WAL/同步模式 | 连接串配置 | 读写并发提升 |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### enableDefaultValue

**说明**: 启用 CodeFirst 默认值支持

**类型**: `boolean`

**默认值**: `true`（自动启用）

**要求**: SqlSugarCore 5.1.4.108-preview23+

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlite-main",
      "optimizationSettings": {
        "enableDefaultValue": "true"
      }
    }
  ]
}
```

**使用场景**:
- CodeFirst 创建表时需要默认值
- 自动填充时间戳等字段

**实体示例**:
```csharp
public class User
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(DefaultValue = "strftime('%Y-%m-%d %H:%M:%S', 'now', 'localtime')")]
    public DateTime CreateTime { get; set; }

    [SugarColumn(DefaultValue = "0")]
    public int Status { get; set; }
}
```

---

### enableDescription

**说明**: 启用 CodeFirst 备注支持

**类型**: `boolean`

**默认值**: `true`（自动启用）

**要求**: SqlSugarCore 5.1.4.108-preview25+

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlite-main",
      "optimizationSettings": {
        "enableDescription": "true"
      }
    }
  ]
}
```

**使用场景**:
- 为表和字段添加备注
- 提高代码可读性

**实体示例**:
```csharp
[SugarTable("users", "用户表")]
public class User
{
    [SugarColumn(ColumnDescription = "用户ID")]
    public int Id { get; set; }

    [SugarColumn(ColumnDescription = "用户名")]
    public string Name { get; set; }
}
```

---

### enableDropColumn

**说明**: 启用 CodeFirst 删除列支持

**类型**: `boolean`

**默认值**: `false`（不启用）

**推荐值**: `true`（仅 .NET Core）

**要求**:
- SqlSugarCore 5.1.4.118-preview04+
- 仅支持 .NET Core

**示例**:
```json
{
  "databases": [
    {
      "name": "sqlite-main",
      "optimizationSettings": {
        "enableDropColumn": "true"
      }
    }
  ]
}
```

**使用场景**:
- CodeFirst 迁移时需要删除列
- 数据库结构调整

**注意**: .NET Framework 不支持此功能

---

## 📝 完整配置示例

### 生产环境配置

```json
{
  "databases": [
    {
      "name": "sqlite-prod",
      "connectionString": "Data Source=./data/production.db;Cache=Shared;Mode=ReadWriteCreate;Journal Mode=WAL;Synchronous=Normal;",
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

**注意**:
- 必须设置 `IsAutoCloseConnection=false`
- 连接关闭后数据丢失

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

**性能**: 加密打开较慢，建议使用单例模式（`IsAutoCloseConnection=false`）+ `lock(db)` 保证线程安全

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

**解决方案**:
1. 清空表数据
2. 重新插入正确格式时间
3. 或修复数据

---

## 🎯 性能优化建议

### 1. 缓存模式

**推荐**: 使用共享缓存

```
Cache=Shared;
```

**性能提升**: 多连接性能提升 **30-50%**

### 2. 日志模式

**推荐**: 使用 WAL 模式

```
Journal Mode=WAL;
```

**优势**:
- ✅ 读写并发性能提升
- ✅ 减少锁等待
- ✅ 提高吞吐量

### 3. 同步模式

**推荐**: 使用 Normal 模式

```
Synchronous=Normal;
```

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

**性能对比**:
- 单条插入: 100 条/秒
- 事务批量: 10,000 条/秒

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
> 加密打开慢，建议 `IsAutoCloseConnection=false`，并在并发时用 `lock` 保护。

### 备份数据库

```csharp
// 解除文件占用
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

// 使用内置备份方法
db.DbMaintenance.BackupDataBase(null, "backup.db");
```

---

**最后更新**: 2025-12-01
**最后更新**: 2025-12-10
