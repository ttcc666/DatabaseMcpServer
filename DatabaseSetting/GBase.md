# GBase 8s（南大通用）数据库配置指南

本文档说明在 DatabaseMcpServer 中配置 GBase 8s。SqlSugar 对 GBase 8s 支持多年，推荐 ODBC 驱动 + SqlSugar.GBaseCore 5.1.4.170+。

---

## 📋 配置方式

### 配置文件 (databases.json) 示例

```json
{
  "databases": [
    {
      "name": "gbase-main",
      "connectionString": "Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8",
      "dbType": "GBase",
      "description": "GBase 8s 主库（ODBC 驱动）",
      "isDefault": false
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

## 🔧 连接字符串

- 端口：19088 或 9088（Docker 常见 19088）
- 新版推荐写法（5.1.4.162+）：
  ```
  Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;
  Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8
  ```
- 旧版 ODBC 写法：
  ```
  Driver={GBase ODBC DRIVER (64-Bit)};Host=localhost;Service=19088;
  Server=gbase01;Database=testdb;Protocol=onsoctcp;
  Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8
  ```

---

## 🧭 初始化

```csharp
// 程序启动时执行一次
InstanceFactory.CustomAssemblies = new System.Reflection.Assembly[]
{
    typeof(GBaseProvider).Assembly
};

var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "上述连接字符串",
    DbType = DbType.GBase,
    IsAutoCloseConnection = true
});
```

---

## 🚀 性能/使用提示

- **驱动版本**：`SqlSugarCore` 5.1.4.158+，`SqlSugar.GBaseCore` 5.1.4.170+（162 存在自动释放问题，170 修复）。
- **大数据插入**：不支持 BulkCopy，推荐分页插入：
  ```csharp
  db.Insertable(objs).PageSize(10 /*字段多用10，字段少用100内*/).ExecuteCommand();
  ```
- **事务支持**：建库语句需带 `WITH LOG`，否则会出现 `Transaction not available`。
- **时间类型**：使用 `DATETIME YEAR TO FRACTION(5)`。
- **Long 类型**：升级到最新驱动。
- **避免频繁 DDL**：主要做 CRUD，尽量避免频繁改表。

---

## ❗ 常见问题

1) **ODBC 安装**：确认已安装 `clientsdk_3.0.0_1_93e040_WIN2003_x86_64`，在“ODBC 64 位数据源”可见。  
2) **自动释放无效**：`SqlSugar.GBaseCore 5.1.4.162` 已知问题，请升级到 `5.1.4.170+`。  
3) **事务不可用**：建库未带 `WITH LOG`。  
4) **时间/长整型问题**：升级到最新 `SqlSugarCore` + `SqlSugar.GBaseCore`。  

---

**最后更新**: 2025-12-10
