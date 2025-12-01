# QuestDB 时序数据库配置指南

本文档详细说明 QuestDB 时序数据库的配置方法和性能优化策略。

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
      "description": "QuestDB 时序数据库主库",
      "isDefault": true,
      "optimizationSettings": {
        "syncWal": false,
        "symbolOptimization": true,
        "batchSize": 10000,
        "partitionStrategy": "DAY"
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
| `host` | 服务器地址 | `localhost` | 可以是 IP 或域名 |
| `port` | 端口号 | `8812` | QuestDB PostgreSQL 协议端口 |
| `username` | 用户名 | `admin` | 数据库用户 |
| `password` | 密码 | `quest` | 用户密码 |
| `database` | 数据库名 | `qdb` | 要连接的数据库 |

### 性能优化参数（推荐）

| 参数 | 说明 | 推荐值 | 性能影响 |
|------|------|--------|---------|
| `ServerCompatibilityMode` | 服务器兼容模式 | `NoTypeLoading` | 提高连接速度 |
| `Pooling` | 启用连接池 | `true` | 连接复用，减少 **95%** 连接开销 |
| `Minimum Pool Size` | 最小连接数 | `1` | 保持最小连接，快速响应 |
| `Maximum Pool Size` | 最大连接数 | `50` | 时序数据库通常不需要大连接池 |
| `Connection Timeout` | 连接超时（秒） | `30` | 避免长时间等待 |

### 可选参数

| 参数 | 说明 | 默认值 | 使用场景 |
|------|------|--------|---------|
| `SSL Mode` | SSL 模式 | `Disable` | 安全连接：`Require` |
| `Command Timeout` | 命令超时（秒） | `30` | 长查询场景 |

---

## 🚀 自动性能优化

DatabaseMcpServer 自动为 QuestDB 应用以下优化：

| 优化项 | 说明 | 效果 |
|--------|------|------|
| WAL 异步写入 | 默认异步写入 | 写入性能提升 **10-100 倍** |
| Symbol 类型优化 | 高重复率字段优化 | 存储空间节省 **50-90%** |
| 批量插入优化 | 批量写入优化 | 大数据导入性能提升 **10-50 倍** |
| 时间分区 | 按时间自动分区 | 查询性能提升 **5-20 倍** |
| 禁用 nvarchar | 自动禁用 nvarchar | 优化存储和性能 |
| 连接池复用 | SqlSugarScope 自动管理 | 连接建立时间从 100ms 降至 5ms |

---

## 🔧 优化配置选项

以下配置选项在 databases.json 的 optimizationSettings 中设置。

### syncWal

**说明**: WAL 同步写入模式

**类型**: `boolean`

**默认值**: `false`（异步写入）

**示例**:
```json
"optimizationSettings": {
  "syncWal": false
}
```

**效果**:
- `false`: 异步写入（默认，高性能）
- `true`: 同步写入（低性能，高一致性）

**性能影响**:
- 异步写入: 100,000+ 行/秒
- 同步写入: 10,000 行/秒

---

### symbolOptimization

**说明**: 启用 Symbol 类型优化

**类型**: `boolean`

**默认值**: `false`

**示例**:
```json
"optimizationSettings": {
  "symbolOptimization": true
}
```

**效果**: 高重复率字段使用 symbol 类型，存储空间节省 50-90%

**注意**: Symbol 类型去重后应小于 60k

---

### batchSize

**说明**: 批量插入大小

**类型**: `integer`

**默认值**: `1000`

**示例**:
```json
"optimizationSettings": {
  "batchSize": 10000
}
```

**效果**: 批量插入性能提升 10-50 倍

**推荐值**:
- 小数据量: 1000
- 中数据量: 5000
- 大数据量: 10000+

---

### partitionStrategy

**说明**: 分区策略

**类型**: `string`

**可选值**: `NONE` / `DAY` / `MONTH` / `YEAR`

**默认值**: `DAY`

**示例**:
```json
"optimizationSettings": {
  "partitionStrategy": "DAY"
}
```

**效果**:
- `NONE`: 不分区
- `DAY`: 按天分区（推荐，高频数据）
- `MONTH`: 按月分区（推荐，中频数据）
- `YEAR`: 按年分区（推荐，低频数据）

---

## 📝 完整配置示例

### 高性能写入配置（生产环境）

```json
{
  "databases": [
    {
      "name": "questdb-prod",
      "connectionString": "host=prod.questdb.com;port=8812;username=app_user;password=secure_password;database=production;ServerCompatibilityMode=NoTypeLoading;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=50;",
      "dbType": "QuestDB",
      "description": "QuestDB 生产环境（高性能写入）",
      "isDefault": true,
      "optimizationSettings": {
        "syncWal": false,
        "symbolOptimization": true,
        "batchSize": 10000,
        "partitionStrategy": "DAY"
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
- ✅ WAL 异步写入（高性能）
- ✅ Symbol 类型优化
- ✅ 大批量插入（10000）
- ✅ 按天分区

**性能指标**:
- 写入速度: 100,000+ 行/秒
- 存储优化: 50-90% 空间节省
- 查询性能: 5-20 倍提升

---

### 高一致性配置（金融场景）

```json
{
  "databases": [
    {
      "name": "questdb-finance",
      "connectionString": "host=localhost;port=8812;username=admin;password=quest;database=finance;ServerCompatibilityMode=NoTypeLoading;",
      "dbType": "QuestDB",
      "description": "QuestDB 金融场景（高一致性）",
      "isDefault": true,
      "optimizationSettings": {
        "syncWal": true,
        "symbolOptimization": true,
        "batchSize": 1000,
        "partitionStrategy": "DAY"
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
- ✅ WAL 同步写入（高一致性）
- ✅ Symbol 类型优化
- ✅ 小批量插入（1000）
- ✅ 按天分区

---

### 开发环境配置

```json
{
  "databases": [
    {
      "name": "questdb-dev",
      "connectionString": "host=localhost;port=8812;username=admin;password=quest;database=dev_db;ServerCompatibilityMode=NoTypeLoading;",
      "dbType": "QuestDB",
      "description": "QuestDB 开发环境",
      "isDefault": true,
      "optimizationSettings": {
        "symbolOptimization": true
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

---

### IoT 场景配置

```json
{
  "databases": [
    {
      "name": "questdb-iot",
      "connectionString": "host=localhost;port=8812;username=iot_user;password=iot123;database=iot_db;ServerCompatibilityMode=NoTypeLoading;",
      "dbType": "QuestDB",
      "description": "QuestDB IoT 场景",
      "isDefault": true,
      "optimizationSettings": {
        "syncWal": false,
        "symbolOptimization": true,
        "batchSize": 50000,
        "partitionStrategy": "MONTH"
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
- ✅ 超大批量插入（50000）
- ✅ 按月分区（长期存储）
- ✅ Symbol 优化（设备 ID 等）

---

## 🔍 常见问题

### 问题 1: 插入后查询不到数据

**错误**: 数据插入成功，但短时间内查询不到

**原因**: WAL 异步写入，数据还未刷新到磁盘

**解决方案**:

**方案 1**: 等待几秒后查询（推荐）
```javascript
// 插入后等待
await sleep(2000);
// 再查询
```

**方案 2**: 启用同步写入（性能下降）
```json
"optimizationSettings": {
  "syncWal": true
}
```

---

### 问题 2: Symbol 类型限制

**错误**: Symbol 类型字段去重后超过 60k

**原因**: QuestDB Symbol 类型有去重限制

**解决方案**:
- 只对高重复率字段使用 symbol 类型
- 低重复率字段使用 string 类型

**示例**:
```sql
-- ✅ 适合 symbol: 设备类型（100 种）
device_type SYMBOL

-- ❌ 不适合 symbol: 用户 ID（100 万个）
user_id STRING
```

---

### 问题 3: 无法删除单条记录

**错误**: DELETE 语句不支持

**原因**: QuestDB 不支持删除单条记录

**解决方案**:

**方案 1**: 使用 TRUNCATE TABLE（清空表）
```sql
TRUNCATE TABLE my_table;
```

**方案 2**: 删除分区
```sql
ALTER TABLE my_table DROP PARTITION '2024-01-01';
```

---

### 问题 4: 连接超时

**错误**:
```
Connection timeout
```

**原因**: 网络延迟或服务器负载高

**解决方案**:
```json
"connectionString": "...;Connection Timeout=60;..."
```

---

### 问题 5: 分区策略选择

**问题**: 如何选择合适的分区策略？

**建议**:

| 数据频率 | 推荐分区 | 原因 |
|---------|---------|------|
| 高频（秒级） | `DAY` | 快速查询，易于管理 |
| 中频（分钟级） | `DAY` 或 `MONTH` | 平衡性能和存储 |
| 低频（小时级） | `MONTH` 或 `YEAR` | 减少分区数量 |

---

## 🎯 性能优化建议

### 1. WAL 写入模式选择

| 场景 | 推荐模式 | 原因 |
|------|---------|------|
| IoT 数据采集 | 异步 | 高吞吐量 |
| 金融交易 | 同步 | 数据一致性 |
| 日志收集 | 异步 | 高性能 |
| 监控告警 | 异步 | 实时性要求不高 |

---

### 2. Symbol 类型使用

**适合 Symbol**:
- ✅ 设备类型（100 种）
- ✅ 地区代码（200 个）
- ✅ 状态码（10 个）
- ✅ 产品类别（50 种）

**不适合 Symbol**:
- ❌ 用户 ID（100 万个）
- ❌ 订单号（无限增长）
- ❌ 时间戳（唯一值）
- ❌ 随机字符串

---

### 3. 批量插入优化

**小数据量** (< 1000 行/秒):
```json
"optimizationSettings": {
  "batchSize": 1000
}
```

**中数据量** (1000-10000 行/秒):
```json
"optimizationSettings": {
  "batchSize": 5000
}
```

**大数据量** (> 10000 行/秒):
```json
"optimizationSettings": {
  "batchSize": 10000
}
```

**超大数据量** (> 100000 行/秒):
```json
"optimizationSettings": {
  "batchSize": 50000
}
```

---

### 4. 分区策略优化

**高频数据** (秒级):
```json
"optimizationSettings": {
  "partitionStrategy": "DAY"
}
```

**中频数据** (分钟级):
```json
"optimizationSettings": {
  "partitionStrategy": "DAY"
}
```
或
```json
"optimizationSettings": {
  "partitionStrategy": "MONTH"
}
```

**低频数据** (小时级):
```json
"optimizationSettings": {
  "partitionStrategy": "MONTH"
}
```
或
```json
"optimizationSettings": {
  "partitionStrategy": "YEAR"
}
```

---

### 5. 查询优化

**时间范围查询**:
```sql
-- ✅ 推荐: 使用时间戳索引
SELECT * FROM sensor_data
WHERE timestamp > '2024-01-01' AND timestamp < '2024-01-02';

-- ❌ 避免: 全表扫描
SELECT * FROM sensor_data WHERE device_id = 'xxx';
```

**聚合查询**:
```sql
-- ✅ 推荐: 使用 SAMPLE BY
SELECT timestamp, avg(temperature)
FROM sensor_data
SAMPLE BY 1h;

-- ❌ 避免: 手动分组
SELECT date_trunc('hour', timestamp), avg(temperature)
FROM sensor_data
GROUP BY 1;
```

---

## 🔗 QuestDB 特性

### 时序特性

| 特性 | 说明 | 使用场景 |
|------|------|---------|
| `SAMPLE BY` | 时间聚合 | 按小时/天/月聚合 |
| `LATEST ON` | 最新记录 | 获取最新状态 |
| `ASOF JOIN` | 时间点连接 | 关联不同频率数据 |
| `DESIGNATED TIMESTAMP` | 指定时间戳列 | 时序优化 |

### 示例

```sql
-- SAMPLE BY: 按小时聚合
SELECT timestamp, avg(temperature)
FROM sensor_data
SAMPLE BY 1h;

-- LATEST ON: 获取每个设备的最新数据
SELECT * FROM sensor_data
LATEST ON timestamp PARTITION BY device_id;

-- ASOF JOIN: 关联不同频率数据
SELECT * FROM trades
ASOF JOIN quotes ON symbol;
```

---

## 📚 相关文档

- [QuestDB .NET 操作](../Doc/donet5_questdb.md)
- [扩展数据库优化策略](../Doc/extending-database-optimization.md)
- [性能优化指南](../Doc/performance-optimization.md)
- [QuestDB 官方文档](https://questdb.io/docs/)
- [主 README](../README.md)

---

## 💡 最佳实践

1. **WAL 模式**: 默认使用异步写入，金融场景使用同步写入
2. **Symbol 类型**: 只对高重复率字段（去重后 < 60k）使用 symbol
3. **批量插入**: 根据数据量选择合适的批量大小（1000-50000）
4. **分区策略**: 根据数据频率选择合适的分区策略（DAY/MONTH/YEAR）
5. **查询优化**: 使用 SAMPLE BY、LATEST ON 等时序特性
6. **删除操作**: 使用 TRUNCATE TABLE 或删除分区，不支持单条删除
7. **连接池**: 时序数据库通常不需要大连接池（50 以内）

---

**最后更新**: 2025-12-01
