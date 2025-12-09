# DuckDB 数据库配置指南

DuckDB 是嵌入式高性能 OLAP 引擎，适合本地分析、CSV/Parquet/内存数据处理，部署类似 SQLite。SqlSugar 支持通过 `SqlSugar.DuckDBCore` 直连。

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
-- 列式 + 向量化，擅长聚合/扫描；避免频繁 DDL。
- 支持 CSV/Parquet 直接查询，配合外部工具（Pandas/R 等）友好。
- 文件方式连接（如 `DataSource=xxx.db`）；内存可用 `DataSource=:memory:`（进程内存，生命周期随进程）。

---

## ⚙️ NuGet 依赖

- `SqlSugarCore`
- `SqlSugar.DuckDBCore`

---

## ❗ 常见问题

1) **找不到 DLL**：启动时预热 `DuckDBProvider`（见上方初始化）。  
2) **文件锁/并发**：DuckDB 适合单进程/低并发写，多读场景；高并发写请谨慎。  
3) **内存模式数据丢失**：`DataSource=:memory:` 数据随进程结束丢失。  

---

**最后更新**: 2025-12-10
