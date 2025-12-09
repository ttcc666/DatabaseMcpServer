# ClickHouse 数据库配置指南

ClickHouse 为列式分析数据库，SqlSugar 提供基础 CRUD/批量写入支持。需安装 `SqlSugar.ClickHouseCore`，并在启动时预热 Provider（见下）。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "clickhouse-main",
      "connectionString": "Host=localhost;Port=8123;User=default;Password=;Database=default",
      "dbType": "ClickHouse",
      "description": "ClickHouse 分析库",
      "isDefault": false,
      "optimizationSettings": {
        "maxPoolSize": "50"
      }
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

## 🚀 自动性能优化

- `ClickHouseOptimizationStrategy` 仅记录连接池提示，保持 `DisableNvarchar=false`。
- 不支持事务；大小写需与库一致；仅 Linux。
- 批量写入需升级：`SqlSugarCore 5.1.3.31-preview11+` 和 `SqlSugar.ClickHouseCore 5.1.3.31+`（高并发可用 `CopyNew().Fastest().BulkCopyAsync`）。

---

## 📦 依赖要求

- NuGet: `SqlSugar.ClickHouseCore` + `SqlSugarCore`
- 预热 Provider（程序启动一次）：
  ```csharp
  InstanceFactory.CustomAssemblies = new[]
  {
      typeof(ClickHouseProvider).Assembly
  };
  ```

---

## 🔧 可选优化配置项（optimizationSettings）

| 配置键 | 类型 | 说明 |
|--------|------|------|
| `maxPoolSize` | int | 连接池最大连接数（提示用） |

---

## 🔗 参考资料

- [ClickHouse（SqlSugar）](https://www.donet5.com/Doc/1/2437)

---

## 🧭 代码示例

```csharp
// 创建客户端
var db = new SqlSugarClient(new ConnectionConfig
{
    DbType = DbType.ClickHouse,
    ConnectionString = "host=localhost;port=8123;user=default;password=;database=default",
    IsAutoCloseConnection = true
});

// 批量写入（同步）
db.Fastest<DC_Scene>().BulkCopy(list);

// 批量写入（高并发异步）
await db.CopyNew().Fastest<DC_Scene>().BulkCopyAsync(list);
```

### 数组类型
```csharp
[SugarColumn(ColumnDataType = "Array(UInt64)", IsArray = true)]
public ulong[] Text { get; set; }
```

### 自定义引擎
```csharp
[SqlSugar.ClickHouse.CKTable(@"engine = MergeTree PARTITION BY toYYYYMM(dt)
    ORDER BY(toYYYYMM(dt))
    SETTINGS index_granularity = 8192;")]
public class CKTest
{
    public string Id { get; set; }
    public DateTime dt { get; set; }
}
```

---

**注意事项**
- 不支持事务。
- 表/列名大小写与库保持一致。
- 推荐使用新版镜像：`docker pull yandex/clickhouse-server`。
- 发布单文件/裁剪需预热 Provider（见上）。

---

**最后更新**: 2025-12-10
