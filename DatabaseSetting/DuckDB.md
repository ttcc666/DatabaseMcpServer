# DuckDB 数据库配置指南

DuckDB 是嵌入式 OLAP 引擎，部署类似 SQLite。已包含 `SqlSugar.DuckDBCore 5.1.4.216`、`DuckDB.NET.Data.Full 1.4.1` 和 `DuckDB.NET.Bindings.Full 1.4.1`，服务统一注册扩展。原生库覆盖 Windows x64/ARM64、Linux x64/ARM64 和 macOS（包内 RID 为 osx）；其他平台需另行验证，见[驱动清单](../Doc/provider-drivers.md)。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "duckdb-main",
      "connectionString": "DataSource=train_services.db",
      "dbType": "DuckDB",
      "description": "DuckDB 嵌入式 OLAP",
      "isDefault": false
    }
  ]
}
```

### MCP 配置

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

## 🧭 初始化示例

```csharp
// 预热 Provider，避免找不到 DLL（程序启动一次即可）
InstanceFactory.CustomAssemblies = new[]
{
    typeof(SqlSugar.DuckDB.DuckDBProvider).Assembly
};

var db = new SqlSugarClient(new ConnectionConfig
{
    DbType = DbType.DuckDB,
    ConnectionString = "DataSource=train_services.db",
    IsAutoCloseConnection = true,
    LanguageType = LanguageType.Default
}, it =>
{
    it.Aop.OnLogExecuting = (sql, pars) =>
    {
        Console.WriteLine(UtilMethods.GetNativeSql(sql, pars));
    };
});
```

---

## 🚀 使用要点

- 嵌入式/单机，无需服务器进程；适合本地分析、数据科学、轻量 BI。
- 列式 + 向量化，擅长聚合/扫描；避免频繁 DDL。
- 支持 CSV/Parquet 直接查询，配合外部工具（Pandas/R 等）友好。
- MCP 每次操作创建并释放独占连接，跨工具读写请使用文件（如 `DataSource=xxx.db`）。`:memory:` 数据库不能保证跨工具保留数据。

---

## ⚙️ NuGet 依赖

- `SqlSugarCore`
- `SqlSugar.DuckDBCore`

---

## ❗ 常见问题

1) **找不到 DLL**：启动时预热 `DuckDBProvider`（见上方初始化）。  
2) **文件锁/并发**：DuckDB 适合单进程/低并发写，多读场景；高并发写请谨慎。  
3) **内存模式数据丢失**：MCP 操作结束关闭连接后，`:memory:` 数据库即可能销毁；跨工具操作应使用文件。

---

**最后更新**: 2025-12-10
