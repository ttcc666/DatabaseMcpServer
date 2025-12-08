# 数据库配置文档索引

本目录包含各数据库类型的详细配置文档，每个文档都包含完整的 JSON 配置和环境变量说明。

---

## 📚 配置文档列表

### 🌐 主流数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **MySQL / MariaDB / TiDB** | [MySQL.md](./MySQL.md) | MySQL 系列数据库配置指南 |
| **SQL Server** | [SQLServer.md](./SQLServer.md) | Microsoft SQL Server 配置指南 |
| **Oracle** | [Oracle.md](./Oracle.md) | Oracle 数据库配置指南 |
| **PostgreSQL** | [PostgreSQL.md](./PostgreSQL.md) | PostgreSQL 数据库配置指南 |
| **SQLite** | [SQLite.md](./SQLite.md) | SQLite 数据库配置指南 |

### 🇨🇳 国产数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **达梦数据库** | [DM.md](./DM.md) | 达梦数据库配置指南 |
| **人大金仓** | [Kdbndp.md](./Kdbndp.md) | 人大金仓数据库配置指南 |
| **GaussDB / OpenGauss** | [GaussDB.md](./GaussDB.md) | 华为 GaussDB/OpenGauss 配置指南 |
| **GBase 8s** | [GBase.md](./GBase.md) | 南大通用 GBase 配置指南 |

### ☁️ 分布式数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **OceanBase** | [OceanBase.md](./OceanBase.md) | MySQL/Oracle 双模式，租户隔离 |
| **TiDB** | [TiDB.md](./TiDB.md) | MySQL 兼容分布式数据库 |

### ⏱️ 时序数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **QuestDB** | [QuestDB.md](./QuestDB.md) | QuestDB 时序数据库配置指南 |
| **TDengine** | [TDengine.md](./TDengine.md) | TDengine 时序数据库配置指南 |

---

### 🧩 其他/兼容数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **GoldenDB** | [GoldenDB.md](./GoldenDB.md) | MySQL 兼容，推荐禁用连接池 |
| **Vastbase** | [Vastbase.md](./Vastbase.md) | PostgreSQL 衍生，建议 `No Reset On Close=true` |
| **ClickHouse** | [ClickHouse.md](./ClickHouse.md) | 列式/分析型数据库 |
| **MongoDB** | [MongoDB.md](./MongoDB.md) | 文档型数据库 |
| **DB2** | [DB2.md](./DB2.md) | IBM DB2 连接指南 |
| **DuckDB** | [DuckDB.md](./DuckDB.md) | 内嵌列式数据库 |
| **Hana** | [Hana.md](./Hana.md) | SAP HANA 连接指南 |
| **HighGo** | [HighGo.md](./HighGo.md) | 瀚高数据库（PG 衍生） |
| **Xugu** | [Xugu.md](./Xugu.md) | 虚谷数据库 |
| **PolarDB** | [PolarDB.md](./PolarDB.md) | MySQL 兼容分布式库 |
| **Doris** | [Doris.md](./Doris.md) | MySQL 兼容列式/HTAP 库 |
| **Oscar** | [Oscar.md](./Oscar.md) | 神通数据库 |
| **Access** | [Access.md](./Access.md) | Microsoft Access（默认策略） |
| **Odbc** | [Odbc.md](./Odbc.md) | 通用 ODBC 连接 |
| **MySqlConnector** | [MySqlConnector.md](./MySqlConnector.md) | MySQL 官方驱动兼容 |
| **Custom** | [Custom.md](./Custom.md) | 自定义/未列出数据库 |

---

## 🆕 新增数据库文档与依赖

已按 `Doc/db.md` 列表抓取官方文档到 `Doc/*.html`，并补充需要的驱动包。常用策略与包对照如下：

| 数据库 | 文档 | 策略/DbType | NuGet 包 |
|--------|------|-------------|----------|
| MongoDB | `Doc/MongoDB.html` | `MongoDb` / `MongoDbOptimizationStrategy` | `SqlSugar.MongoDbCore` |
| TDengine | `Doc/TDengine.html` | `TDengine` / `TdengineOptimizationStrategy` | `SqlSugar.TDengineCore` |
| GBase 8s | `Doc/Gbase.html` | `GBase` / `GBaseOptimizationStrategy` | `SqlSugar.GBaseCore` |
| ClickHouse | `Doc/ClickHouse.html` | `ClickHouse` / `ClickHouseOptimizationStrategy` | `SqlSugar.ClickHouseCore` |
| GaussDB Native | `Doc/Gauss.html` | `GaussDBNative` / `GaussDbOptimizationStrategy` | `SqlSugar.GaussDBNativeCore` |
| 虚谷数据库 | `Doc/Xugu.html` | `Xugu` / `XuguOptimizationStrategy` | `SqlSugar.XuguCoreNew` |

其他 MySQL 兼容库（PolarDB、Doris、TDSQL、MySqlConnector、GoldenDB 等）复用 `MySqlOptimizationStrategy`，无需额外包；Odbc/Access/Custom 使用默认策略。

---

## 🔧 配置方式

DatabaseMcpServer 使用 JSON 配置文件管理数据库连接。

### 配置文件 (databases.json)

**适用场景**: 单数据库或多数据库管理

**示例**:
```json
{
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "Server=localhost;...",
      "dbType": "MySql",
      "description": "MySQL 主库",
      "isDefault": true,
      "optimizationSettings": {
        "option1": "value1"
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

---

## 📊 快速对比

### 连接字符串格式

| 数据库 | 连接字符串示例 |
|--------|---------------|
| MySQL | `Server=localhost;Port=3306;Database=mydb;User=root;Password=pass;` |
| SQL Server | `Server=localhost;Database=mydb;User Id=sa;Password=pass;Encrypt=True;TrustServerCertificate=True;` |
| Oracle | `Data Source=localhost/orcl;User ID=system;Password=pass;Pooling=true;` |
| PostgreSQL | `Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=pass;` |
| SQLite | `Data Source=./data/mydb.db;Cache=Shared;Mode=ReadWriteCreate;` |

## 🔧 优化配置选项

从 2.0.0 版本开始，所有优化配置都在 databases.json 的 optimizationSettings 中设置。

### 配置格式

```json
{
  "databases": [
    {
      "name": "db-name",
      "connectionString": "...",
      "dbType": "DbType",
      "optimizationSettings": {
        "option1": "value1",
        "option2": "value2"
      }
    }
  ]
}
```

### 各数据库支持的优化选项

| 数据库 | 配置键名 | 说明 |
|--------|---------|------|
| MySQL / MariaDB | `enableBulkCopy` | 启用 BulkCopy（需连接串 AllowLoadLocalInfile=true） |
| MySQL / MariaDB | `maxPoolSize` | 连接池最大连接数 |
| MySQL / MariaDB | `charset` | 字符集（默认 utf8mb4） |
| MySQL / MariaDB | `enableSsl` | 启用 SSL 连接 |
| MySQL / MariaDB | `allowUserVariables` | 允许用户变量 |
| SQL Server | `disableNvarchar` | 禁用 nvarchar 优化索引 |
| Oracle | `camelCase` | 使用驼峰表名 |
| Oracle | `enableIdentity` | 启用原生自增（12C+） |
| Oracle | `maxParamLength` | 参数名长度限制（Oracle 11） |
| PostgreSQL | `autoToLower` | 表名自动转小写 |
| PostgreSQL | `enableILike` | 启用 ILike 不区分大小写 |
| PostgreSQL | `identityStrategy` | 自增策略（Serial/Identity） |
| SQLite | `enableDefaultValue` | CodeFirst 默认值支持 |
| SQLite | `enableDescription` | CodeFirst 备注支持 |
| SQLite | `enableDropColumn` | CodeFirst 删除列支持 |
| 达梦 | `lowercaseTables` | 使用小写表名 |
| 达梦 | `dockerMysqlMode` | Docker MySQL 兼容模式 |
| 达梦 | `clobOptimization` | Clob 类型优化 |
| 达梦 | `schema` | Schema 隔离 |
| 达梦 | `maxPoolSize` | 连接池最大连接数 |
| 人大金仓 | `mode` | 兼容模式（Oracle/MySQL/PostgreSQL/SqlServer） |
| 人大金仓 | `camelCase` | 使用驼峰表名 |
| 人大金仓 | `enableCursor` | 启用游标支持 |
| 人大金仓 | `enableJson` | 启用 JSON 类型 |
| 人大金仓 | `enableGeometry` | 启用 Geometry/Postgis |
| 人大金仓 | `enableArray` | 启用数组类型 |
| 人大金仓 | `maxPoolSize` | 连接池最大连接数 |
| 人大金仓 | `schema` | Schema 隔离 |
| GaussDB | `nativeDriver` | 使用原生驱动 |
| GaussDB | `isOpenGauss` | 标识 OpenGauss 数据库 |
| GaussDB | `schema` | Schema 隔离 |
| GaussDB | `typeMapping` | 数据类型映射优化 |
| GaussDB | `batchSize` | 批量操作大小 |
| GaussDB | `maxPoolSize` | 连接池最大连接数 |
| QuestDB | `syncWal` | WAL 同步写入 |
| QuestDB | `symbolOptimization` | Symbol 类型优化 |
| QuestDB | `batchSize` | 批量插入大小 |
| QuestDB | `partitionStrategy` | 分区策略（DAY/MONTH/YEAR） |
| GBase | `batchPageSize` | 分页批量写入大小（不支持 BulkCopy，推荐 10-100） |
| GBase | `enableBulkCopy` | 仅提示用，GBase ODBC 不支持 BulkCopy，保持 false |
| GBase | `dbLocale` / `clientLocale` | 记录 Locale 编码，避免字符集不一致 |
| GBase | `maxPoolSize` | 连接池最大连接数 |
| OceanBase | `disablePooling` | 禁用连接池（部分租户不支持池化） |
| OceanBase | `disableNvarchar` | 可选禁用 nvarchar（少数兼容模式需要） |
| OceanBase | `enableHints` | 启用 Optimizer Hints 支持 |
| OceanBase | `tenantMode` | 租户兼容模式（mysql/oracle） |
| OceanBase | `enableBulkCopy` | 批量写入优化 |
| OceanBase | `maxPoolSize` | 连接池最大连接数 |
| TiDB | `enableHints` | 启用 TiDB Optimizer Hints |
| TiDB | `pessimisticTxn` | 启用悲观事务模式 |
| TiDB | `maxPoolSize` | 连接池最大连接数 |
| TiDB | `enableBulkCopy` | 批量导入优化 |
| TiDB | `disableNvarchar` | 少数特殊环境禁用 nvarchar（官方提示兼容项） |

详见各数据库配置文档。

---

## 🎯 性能优化要点

### MySQL / MariaDB / TiDB
- ✅ 字符集: `Charset=utf8mb4`
- ✅ 连接池: `Pooling=true;Min Pool Size=1;Max Pool Size=100`
- ✅ 批量导入: `AllowLoadLocalInfile=true`
- ✅ 用户变量: `Allow User Variables=True`

### SQL Server
- ✅ 新版驱动: `Encrypt=True;TrustServerCertificate=True`
- ✅ 自动 NoLock: 并发性能提升 60-70%
- ✅ 可选禁用 nvarchar: 索引性能提升 20-30%
- ✅ 连接池: `Min Pool Size=1;Max Pool Size=100`

### Oracle
- ✅ 连接池（必需）: `Pooling=true;Min Pool Size=5;Max Pool Size=150`
- ✅ 表名处理: 智能大小写转换
- ✅ 原生自增: Oracle 12C+ 支持
- ✅ 参数名限制: Oracle 11 兼容

### PostgreSQL
- ✅ 连接池: `Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100`
- ✅ 表名规范: 自动转小写
- ✅ ILike 支持: 不区分大小写查询
- ✅ 自增策略: Serial/Identity 可选

### SQLite
- ✅ 共享缓存: `Cache=Shared`（性能提升 30-50%）
- ✅ CodeFirst 增强: 默认值、备注、删除列
- ✅ 内存模式: `Data Source=:memory:`
- ✅ 加密模式: `Password=your_password`

### 达梦数据库
- ✅ 智能表名处理: 默认大写，可配置小写
- ✅ Docker 模式兼容: 自动识别 MySQL 兼容模式
- ✅ Clob 类型优化: 大文本字段优化
- ✅ Schema 支持: 多租户隔离

### 人大金仓
- ✅ 多模式兼容: Oracle/MySQL/PostgreSQL/SqlServer
- ✅ 游标支持: 存储过程游标参数
- ✅ JSON 类型: 现代应用开发
- ✅ Geometry 支持: 地理信息系统

### GaussDB / OpenGauss
- ✅ 原生驱动: 访问 GaussDB 特有特性
- ✅ Npgsql 兼容: 成熟稳定，生态丰富
- ✅ 数据类型映射: JSON、Geometry 等
- ✅ 批量操作优化: 大数据导入性能提升

### GBase 8s
- ✅ ODBC 驱动: 安装 `SqlSugar.GBaseCore` 并注册 `GBaseProvider`
- ✅ 分页批量写入: 使用 `Insertable(...).PageSize(10-100)`（不支持 BulkCopy）
- ✅ Locale 一致: `Db_locale` / `Client_locale` 保持相同编码
- ✅ 连接池: 常见场景 `Max Pool Size` 建议 50 左右

### QuestDB
- ✅ WAL 异步写入: 写入性能提升 10-100 倍
- ✅ Symbol 类型: 存储空间节省 50-90%
- ✅ 批量插入: 大数据导入性能提升 10-50 倍
- ✅ 时间分区: 查询性能提升 5-20 倍

### OceanBase
- ✅ MySQL 模式: 默认支持 utf8mb4 与 MySQL 协议
- ✅ 连接池开关: `disablePooling` 兼容不支持池化的租户
- ✅ 租户模式: `tenantMode` 区分 mysql/oracle 兼容
- ✅ Optimizer Hints: 支持查询计划调优
- ✅ 批量写入: `enableBulkCopy` 优化大批量导入

### TiDB
- ✅ MySQL 兼容: 直接复用 MySQL 驱动
- ✅ 悲观事务: `pessimisticTxn` 适配高并发冲突场景
- ✅ 连接池: `maxPoolSize`、`Pooling` 建议更大池化配置
- ✅ 优化提示: `enableHints` 支持 TiFlash/索引选择
- ✅ 批量导入: `enableBulkCopy`、`AllowLoadLocalInfile=true` 加速写入

---

## 📖 文档结构

每个配置文档包含以下内容：

1. **配置方式**
   - JSON 配置文件示例
   - MCP 配置示例

2. **连接字符串参数详解**
   - 必需参数
   - 性能优化参数
   - 可选参数

3. **自动性能优化**
   - DatabaseMcpServer 自动应用的优化

4. **优化配置选项**
   - optimizationSettings 配置项详解
   - 各选项的作用和使用场景

5. **完整配置示例**
   - 生产环境配置
   - 开发环境配置
   - 特殊场景配置

6. **常见问题**
   - 错误原因分析
   - 解决方案

7. **性能优化建议**
   - 连接池配置
   - 批量操作优化
   - 查询优化

8. **特殊类型支持**
   - 数据库特有类型的使用方法

---

## 🚀 快速开始

### 1. 选择数据库类型

根据你使用的数据库，选择对应的配置文档：

**主流数据库**:
- MySQL 系列 → [MySQL.md](./MySQL.md)
- SQL Server → [SQLServer.md](./SQLServer.md)
- Oracle → [Oracle.md](./Oracle.md)
- PostgreSQL → [PostgreSQL.md](./PostgreSQL.md)
- SQLite → [SQLite.md](./SQLite.md)

**国产数据库**:
- 达梦数据库 → [DM.md](./DM.md)
- 人大金仓 → [Kdbndp.md](./Kdbndp.md)
- GaussDB/OpenGauss → [GaussDB.md](./GaussDB.md)
- GBase 8s → [GBase.md](./GBase.md)

**时序数据库**:
- QuestDB → [QuestDB.md](./QuestDB.md)

### 2. 准备配置文件

- 创建 databases.json 配置文件
- 支持单数据库或多数据库配置

### 3. 复制配置示例

每个文档都提供了完整的配置示例，可以直接复制使用。

### 4. 根据需要调整参数

参考文档中的参数详解，根据实际情况调整配置。

---

## 📚 相关文档

- [扩展数据库优化策略](../Doc/extending-database-optimization.md)
- [主 README](../README.md)

---

## 💡 提示

- 📖 每个配置文档都是独立的，可以单独查阅
- 🔧 从 2.0.0 版本开始，统一使用 JSON 配置文件管理数据库连接
- ⚡ 文档中的性能优化建议都经过实际测试验证
- 🎯 遇到问题时，先查看对应数据库的"常见问题"章节
- 🔄 优化配置选项从环境变量迁移到 optimizationSettings 配置项

---

**最后更新**: 2025-12-08
