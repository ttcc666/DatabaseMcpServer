# QuestDB 数据库配置指南

本文档说明如何在 DatabaseMcpServer 中配置和使用 QuestDB（时序数据库）。**QuestDB 更适合追加写入与聚合查询，不推荐频繁 DDL/Truncate。**

---

## 📋 配置方式

### 配置文件 (databases.json)

#### 完整配置示例

```json
{
  "databases": [
    {
      "name": "questdb-main",
      "connectionString": "host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;",
      "dbType": "QuestDB",
      "description": "QuestDB 时序库（追加写入 + 聚合查询）",
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

## 🔧 连接字符串参数

| 参数 | 说明 | 示例 |
|------|------|------|
| `host` | 服务器地址 | `localhost` |
| `port` | 端口 | `8812` |
| `username` | 用户名 | `admin` |
| `password` | 密码 | `quest` |
| `database` | 数据库 | `qdb` |
| `ServerCompatibilityMode` | 模式 | `NoTypeLoading`（官方示例） |

---

## 🚀 使用要点 / 注意事项

- **避免频繁 DDL/Truncate**：QuestDB 对 DDL/分区/Truncate 较敏感，频繁操作可能导致锁表；建表后尽量保持结构稳定。
- **删除操作限制**：不支持常规 DELETE，需使用 `TRUNCATE TABLE` 或删除分区。
- **追加写入场景**：适合时序/日志/金融行情，建议追加写入 + 聚合查询。
- **Symbol 类型**：仅用于高重复值字段，去重后建议 < 6 万；低重复度不要使用 symbol，以免插入变慢。
- **分表/分区**：按时间分表（`TimeDbSplitField`）或使用时间分区，避免大表膨胀；分表后如遇锁表，需按官方建议重启或解除锁。
- **WAL/并发插入**：高并发写入可考虑 WAL 表；如遇 `table busy`，参考官方 issue/文档处理。
- **RestAPI Bulk**：可选安装 `SqlSugar.QuestDb.RestAPI`，使用 `db.RestApi().BulkCopy(...)` 进行高吞吐写入；默认 ADO 兼容性更高但并发略低。

---

## 🧭 示例代码

```csharp
var db = new SqlSugarClient(new ConnectionConfig
{
    DbType = DbType.QuestDB,
    ConnectionString = "host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;",
    IsAutoCloseConnection = true
});

// 基础查询
var list = db.Queryable<MyPoint>().ToList();

// 分组聚合
var agg = db.Queryable<MyPoint>()
    .GroupBy(it => it.Name)
    .Select(it => new { it.Name, Count = SqlFunc.AggregateCount(it.Value) })
    .ToList();
```

实体示例（时间分表 + symbol）：
```csharp
[SugarIndex(null, nameof(Telemetry.Tag), OrderByType.Asc)]
public class Telemetry
{
    public long Id { get; set; }

    [SugarColumn(ColumnDataType = "symbol")]
    public string Tag { get; set; } = string.Empty; // 高重复度字段

    [TimeDbSplitField(DateType.Day)]
    [SugarColumn(IsOnlyIgnoreUpdate = true)]
    public DateTime Ts { get; set; }

    public double Value { get; set; }
}
```

---

## ❗ 常见问题

1) **LOCK TABLE / table busy**：避免在线改表结构或频繁 Truncate；必要时重启服务或参考官方解锁方案。  
2) **插入后短暂查询不到**：WAL/异步写入导致延迟，等待或关闭 WAL（并发会下降）。  
3) **STRING -> BOOLEAN / 类型映射错误**：确认字段类型与参数一致，必要时检查 QuestDB 版本的已知问题。  
4) **Symbol 写入慢**：检查字段重复度，低重复度不要使用 symbol。  

---

**最后更新**: 2025-12-10
