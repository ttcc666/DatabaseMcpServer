# GBase 8s 数据库配置指南

本文档基于 `Doc/Gbase.md` 的官方说明，总结了 GBase 8s 的连接字符串写法、性能要点和可选优化项。GBase 通过 ODBC 方式连接，不支持 BulkCopy，需要使用分页批量写入。

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "gbase-main",
      "connectionString": "Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8;Min Pool Size=1;Max Pool Size=50;",
      "dbType": "GBase",
      "description": "GBase 8s 主库（ODBC 连接）",
      "isDefault": true,
      "optimizationSettings": {
        "batchPageSize": "50",
        "enableBulkCopy": "false",
        "dbLocale": "zh_CN.utf8",
        "clientLocale": "zh_CN.utf8"
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

| 参数 | 说明 | 示例 | 备注 |
|------|------|------|------|
| `Host` | 服务器地址 | `localhost` | OB / Docker 默认端口常为 19088 或 9088 |
| `Service` | 服务端口 | `19088` | GBase 默认监听端口 |
| `Server` | 实例名 | `gbase01` | 对应实例标识 |
| `Database` | 数据库名 | `testdb` | 目标数据库 |
| `Protocol` | 协议 | `onsoctcp` | ODBC 连接协议 |
| `Uid` | 用户名 | `gbasedbt` | 数据库用户 |
| `Pwd` | 密码 | `GBase123` | 用户密码 |
| `Db_locale` | 数据库 Locale | `zh_CN.utf8` | 与库一致的编码 |
| `Client_locale` | 客户端 Locale | `zh_CN.utf8` | 与客户端编码保持一致 |

**性能参数（推荐）**

| 参数 | 示例 | 说明 |
|------|------|------|
| `Min Pool Size` | `1` | 维持最小连接 |
| `Max Pool Size` | `50` | 时序/OLTP 常用池大小 |

---

## 🚀 自动性能优化

DatabaseMcpServer 会自动：
- 使用 GBase 专属策略并保留 nvarchar 支持
- 识别 BulkCopy 配置并提示改用 `Insertable().PageSize()` 分页批量写入
- 记录 Locale 与连接池配置，便于排查编码与池化问题

---

## 🔧 优化配置选项

以下配置项在 `optimizationSettings` 中设置：

### batchPageSize
- **类型**: `integer`，默认 `50`
- **作用**: 设置 `Insertable().PageSize()` 的分页大小，推荐 10-100
- **示例**:
```json
"optimizationSettings": {
  "batchPageSize": "50"
}
```

### enableBulkCopy
- **类型**: `boolean`，默认 `false`
- **作用**: 仅用于提示。GBase 不支持 BulkCopy，设置为 `true` 会记录警告。
- **示例**:
```json
"optimizationSettings": {
  "enableBulkCopy": "false"
}
```

### dbLocale / clientLocale
- **类型**: `string`
- **作用**: 记录并校验连接字符串中的 Locale，避免编码不一致。
- **示例**:
```json
"optimizationSettings": {
  "dbLocale": "zh_CN.utf8",
  "clientLocale": "zh_CN.utf8"
}
```

### maxPoolSize
- **类型**: `integer`
- **作用**: 记录连接池上限，便于性能调优。

---

## 📝 常见问题

1) **BulkCopy 不可用**  
GBase ODBC 驱动不支持 BulkCopy，请使用 `Insertable(list).PageSize(50).ExecuteCommand()` 进行分页批量写入。

2) **Transaction not available**  
建库时需包含 `WITH LOG` 以启用事务支持。

3) **长文本/时间类型问题**  
确保使用官方驱动版本（如 `SqlSugar.GBaseCore 5.1.4.170+`）并保持 `Db_locale` / `Client_locale` 一致。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`、`SqlSugar.GBaseCore`
- 驱动: 安装 `GBase ODBC DRIVER (64-Bit)` 并在系统 ODBC 管理器可见
- 启动前注册提供程序（程序入口执行一次）:
```csharp
InstanceFactory.CustomAssemblies = new[] { typeof(GBaseProvider).Assembly };
```

---

## 🔗 参考资料

- [Doc/Gbase.md](../Doc/Gbase.md)
- [扩展数据库优化策略](../Doc/extending-database-optimization.md)

---

**最后更新**: 2025-12-08
