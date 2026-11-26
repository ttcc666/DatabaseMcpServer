# 数据库配置文档索引

本目录包含各数据库类型的详细配置文档，每个文档都包含完整的 JSON 配置和环境变量说明。

当前源码的实际能力以 [ORM 配置约定](../Doc/orm-configuration.md) 和 `get_database_capabilities` 为准。HG/GBase/TDengine/DuckDB 扩展已安装，[驱动清单](../Doc/provider-drivers.md)说明原生客户端要求。MongoDB 已提供六个[集合工具](../Doc/mongodb-collections.md)；其他 SqlSugar 功能介绍不代表 MCP 已开放。池、TLS、Schema 等优化键不覆盖连接字符串，BulkCopy/原生数组开关未接入。

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
| **MongoDB** | [MongoDB.md](./MongoDB.md) | 文档型数据库 |

### 🇨🇳 国产数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **达梦数据库** | [DM.md](./DM.md) | 达梦数据库配置指南 |
| **人大金仓** | [Kdbndp.md](./Kdbndp.md) | 人大金仓数据库配置指南 |
| **GaussDB / OpenGauss** | [GaussDB.md](./GaussDB.md) | 华为 GaussDB/OpenGauss 配置指南 |
| **神通数据库** | [Oscar.md](./Oscar.md) | 神通数据库配置指南 |
| **瀚高数据库** | [HighGo.md](./HighGo.md) | 瀚高数据库（PG 衍生） |
| **Vastbase** | [Vastbase.md](./Vastbase.md) | PostgreSQL 衍生，建议 `No Reset On Close=true` |
| **GoldenDB** | [GoldenDB.md](./GoldenDB.md) | MySQL 兼容，推荐禁用连接池 |
| **GBase 8s** | [GBase.md](./GBase.md) | 南大通用，ODBC 驱动 |
| **PolarDB** | [PolarDB.md](./PolarDB.md) | MySQL 兼容 |

### ☁️ 分布式数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **OceanBase** | [OceanBase.md](./OceanBase.md) | MySQL/Oracle 双模式，租户隔离 |
| **TiDB** | [TiDB.md](./TiDB.md) | MySQL 兼容分布式数据库 |
| **TDSQL** | [MySQL.md](./MySQL.md) | MySQL 兼容模式（`dbType: "TDSQL"`） |

### 📊 分析型数据库

| 数据库 | 文档 | 说明 |
|--------|------|------|
| **ClickHouse** | [ClickHouse.md](./ClickHouse.md) | 列式/分析型数据库 |
| **Doris** | [Doris.md](./Doris.md) | Apache Doris OLAP（FE 默认端口 9030） |
| **QuestDB** | [QuestDb.md](./QuestDb.md) | 时序数据库，追加写入 + 聚合场景 |
| **DuckDB** | [DuckDB.md](./DuckDB.md) | 嵌入式列式 OLAP |
| **TDengine** | [TDengine.md](./TDengine.md) | 时序库，ms/us/ns 精度 |

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

### 优化选项的作用范围

以 `get_database_capabilities` 返回的 `optimizationSettings` 为准；完整约定见 [ORM 配置](../Doc/orm-configuration.md)。

| 状态 | 常见选项 | 当前行为 |
| --- | --- | --- |
| mapped | disableNvarchar、enableNoLock、autoToLower、enableILike、camelCase 等 | 映射到相应 provider 的 ConnMoreSettings；作用于对应 ORM 路径，不改写原始 SQL |
| mapped（CodeFirst-only） | SQLite 的 enableDefaultValue、enableDescription、enableDropColumn | 仅用于 CodeFirst；本服务 schema 工具使用 DbMaintenance |
| advisory | maxPoolSize、disablePooling、enableSsl、charset、allowUserVariables、schema、noResetOnClose | 保留并诊断；实际值必须在驱动连接字符串中设置 |
| advisory | nativeDriver、tenantMode | 不选择驱动或切换租户模式；通过 dbType 和连接串选择 |
| not-applicable | enableBulkCopy、enableCursor、enableJson、enableGeometry、enableArray、clobOptimization、typeMapping、batchSize、enableHints、pessimisticTxn | 当前 MCP 没有相应开关实现，设置为 true 也不会开启功能 |
| unknown | 未识别键 | 保留配置、隐藏值，不宣称生效 |

GaussDBNative 的 databaseModel / isOpenGauss 选择原生 provider 的数据库模式，冲突会报错；GaussDB/OpenGauss 的驱动选择仍由 dbType 决定。

---

## 🎯 连接与执行要点

- 每次操作独占 SqlSugarClient，物理连接由驱动池复用；连接池、TLS、Schema 使用目标驱动的连接串语法。
- SQL Server 原始查询不会自动添加 WITH(NOLOCK)。生产 TLS 应校验证书；TrustServerCertificate=True 只适用于受控开发环境。
- SQLite 使用文件库跨请求保存数据；普通 :memory: 数据库不会在独立租约间共享。CodeFirst 开关不扩大 MCP 的 schema 能力。
- insert_row 只支持 SQL Server、MySQL、SQLite 的单行整数自增插入；SQL 批命令不是 BulkCopy。Mongo 文档批量写入使用 mongo_insert_many。
- TiDB、OceanBase、PolarDB 等兼容产品的能力按配置产品判断，不能根据驱动相同推断分页、事务、过程或实库验证已支持。
- 性能与数据量、查询、索引、网络及服务器配置有关。文档不承诺固定提升比例；已有验证范围见[驱动清单](../Doc/provider-drivers.md)。

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

3. **驱动与 ORM 设置**
   - 实际映射项及生效范围

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
- ⚡ 仅将有日期和环境记录的结果视为验证证据；其他配置建议需在目标环境确认
- 🎯 遇到问题时，先查看对应数据库的"常见问题"章节
- 🔄 优化配置选项从环境变量迁移到 optimizationSettings 配置项

---

**最后更新**: 2026-11-26
