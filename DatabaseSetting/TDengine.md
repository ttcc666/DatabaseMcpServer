# TDengine 数据库配置指南

已包含 `SqlSugar.TDengineCore 4.18.46`、`TDengine.Ado.Data 3.1.7` 和 `TDengine.Connector 3.1.6`，服务统一注册扩展。当前 SqlSugar ADO 路径使用原生连接，部署主机仍需匹配版本的 TDengine 客户端 SDK，见[驱动清单](../Doc/provider-drivers.md)。下文实体、Tag 和 BulkCopy 示例是 SqlSugar API 用法，不代表 MCP 已提供对应专用工具。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "tdengine-main",
      "connectionString": "Host=localhost;Port=6030;Username=root;Password=taosdata;Database=power",
      "dbType": "TDengine",
      "description": "TDengine 时序库（原生连接 + ms/us/ns 精度）",
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

## 🔧 初始化

```csharp
// 程序启动时（只需一次）
InstanceFactory.CustomAssemblies = new[]
{
    typeof(TDengineProvider).Assembly
};

var db = new SqlSugarClient(new ConnectionConfig
{
    DbType = DbType.TDengine,
    ConnectionString = "Host=localhost;Port=6030;Username=root;Password=taosdata;Database=power",
    IsAutoCloseConnection = true
});
```

---

## 🚀 关键要点

- **SDK/驱动**：需安装 TDengine 客户端 SDK（版本不低于服务端）。  
- **时间精度**：默认毫秒；微秒需连接串 `TsType=config_us` + `DateTime16`；纳秒用 `TsType=config_ns` + `DateTime19`，建库/表保持一致。  
- **子表/Tag**：支持 `STable + Tag` 自动建子表，批量写入可用 `Fastest().BulkCopy()` / `RestApi().BulkCopy()`。  
- **表模式**：默认小写表；如需驼峰，关闭 `PgSqlIsAutoToLower/CodeFirst`（一般保持默认）。  
- **DDL/发布**：尽量避免频繁 DDL；单文件/裁剪发布需预热 `InstanceFactory.CustomAssemblies`。  
- **不支持 left join**：请使用子表/超级表查询或拆分查询。  
- **批量插入**：大数据建议 `Fastest().BulkCopy`；固定子表用 `Insertable().AS(table)`；按 Tag 分表可用 `SetTDengineChildTableName` 或 `TDengineFastBuilder.SetTags`。  
- **事务/锁**：时序场景尽量追加写入，避免频繁结构变更。  

---

## 🧭 示例

### 基础查询/写入
```csharp
var list = db.Queryable<MyTable02>().Where(it => it.name == "测试").ToList();
db.Insertable(new MyTable02 { ts = DateTime.Now, name = "a", phase = 1.2f }).ExecuteCommand();
```

### Hints/函数示例
```csharp
var agg = db.Queryable<MyTable02>()
    .GroupBy(it => it.name)
    .Select(it => new { it.name, Count = SqlFunc.AggregateCount(it.ts) })
    .ToList();
```

### 子表/Tag 批量
```csharp
db.Fastest<MyTable02>()
  .AS("MyTable02_a")
  .BulkCopy(list);
```

### 按 Tag 自动子表
```csharp
db.Insertable(list)
  .SetTDengineChildTableName((stable, row) => $"{stable}_{row.Tag1}")
  .ExecuteCommand();
```

---

## ❗ 常见问题

1) **时间过滤/精度不一致**：微秒/纳秒需连接串 `TsType=config_us/config_ns` 且实体用 `DateTime16/DateTime19`。  
2) **发布缺少 DLL**：单文件/裁剪发布需在启动时预热 `TDengineProvider`。  
3) **插入后短暂查询不到/WAL 延迟**：TDengine 写入可能延迟，稍候查询。  
4) **连接失败**：确认 SDK 客户端已安装且版本不低于服务端；检查端口/账号。  

---

**最后更新**: 2025-12-10
