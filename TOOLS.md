# DatabaseMcpServer Tools 列表

本文档列出了 DatabaseMcpServer 提供的所有 MCP 工具及其功能说明。

## 目录

- [连接管理工具 (ConnectionTools)](#连接管理工具-connectiontools)
- [查询工具 (QueryTools)](#查询工具-querytools)
- [命令执行工具 (CommandTools)](#命令执行工具-commandtools)
- [架构管理工具 (SchemaTools)](#架构管理工具-schematools)
- [Excel 导出工具 (ExcelExportTools)](#excel-导出工具-excelexporttools)
- [文档生成工具 (DocumentationTools)](#文档生成工具-documentationtools)

---

## 连接管理工具 (ConnectionTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `TestConnection` | 在当前活动连接上运行 SELECT 1 测试，返回连接状态、当前数据库和数据库类型 |
| `TestConnectionByName` | 使用指定的数据库名称创建连接并测试，验证特定连接是否健康 |
| `GetDatabaseConfig` | 获取数据库配置摘要，包括活动连接名称、描述、数据库类型、掩码连接字符串和模式元数据 |
| `ValidateConfiguration` | 验证环境变量或配置文件是否能产生可用的连接 |
| `ListDatabases` | 列出所有已配置的数据库连接（名称、类型、描述、默认标志、当前标志） |
| `SwitchDatabase` | 切换活动连接到指定的数据库名称 |
| `GetCurrentDatabase` | 返回当前活动的数据库连接名称和数据库类型 |
| `HealthCheck` | 对所有已配置的数据库连接执行全面的健康检查，测试连接性和响应时间 |
| `TestConnectionWithRetry` | 带自动重试机制的连接测试，支持指数退避重试 |

---

## 查询工具 (QueryTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `SqlQuery` | 执行只读 SQL 语句，支持危险操作检测和 JSON 参数绑定，返回行数和数据 |
| `SqlQuerySingle` | 执行只读 SQL 语句并仅返回第一行（或 null） |
| `GetDataReader` | 执行 SQL 并通过 DataReader 流式读取结果，将每行转换为字典 |
| `GetDataSetAll` | 执行可能包含多个 SELECT 语句的 SQL，返回每个结果集及其行数 |
| `GetScalar` | 返回 SQL 语句的第一行第一列值，适用于 COUNT/SUM 等标量查询 |
| `GetString` | 返回第一行第一列值作为字符串 |
| `GetInt` | 返回第一行第一列值转换为整数 |
| `GetLong` | 返回第一行第一列值转换为长整数 |
| `GetDouble` | 返回第一行第一列值转换为双精度浮点数 |
| `GetDecimal` | 返回第一行第一列值转换为 decimal |
| `GetDateTime` | 返回第一行第一列值转换为 DateTime，适用于时间戳 |
| `SqlQueryMultiple` | 执行必须返回两个结果集的 SQL（用分号分隔），返回两个结果集 |
| `SqlQueryWithInParameter` | 绑定 JSON 数组到 IN 参数，安全执行 IN 子句查询 |

---

## 命令执行工具 (CommandTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `ExecuteCommand` | 执行 INSERT/UPDATE/DELETE SQL，支持危险操作检测和 JSON 参数绑定，返回受影响行数 |
| `InsertData` | 向指定表插入单行数据，通过 JSON 对象传递列值 |
| `UpdateData` | 使用 JSON 对象更新表中的行，配合 WHERE 子句过滤 |
| `DeleteData` | 从表中删除满足 WHERE 子句的行 |
| `ExecuteTransaction` | 在一个事务中执行 SQL 命令数组（每个命令都会检查危险操作） |
| `CallStoredProcedure` | 调用指定的存储过程，支持可选的 JSON 参数 |
| `CallStoredProcedureWithOutput` | 调用存储过程并处理输出参数，返回结果集和输出参数值 |
| `ExecuteCommandWithGo` | 执行包含 GO 批处理的 SQL Server 脚本，自动分割脚本 |
| `BatchExecuteCommands` | 在单个长连接上批量执行 SQL 命令数组，支持每个命令的可选参数 |

---

## 架构管理工具 (SchemaTools)

### 数据库信息查询

| 工具名称 | 功能描述 |
|---------|---------|
| `GetDataBaseList` | 获取实例可见的所有数据库列表 |
| `GetViewInfoList` | 获取所有视图的元数据（名称、定义、架构） |
| `GetTableInfoList` | 返回每个表的基本信息（名称、描述、创建时间） |
| `GetColumnInfosByTableName` | 获取指定表的所有列信息（数据类型、长度、可空标志等） |
| `GetIsIdentities` | 返回指定表的所有自增列 |
| `GetPrimaries` | 返回指定表的主键元数据，包括约束名称、列和序号 |

### 存在性检查

| 工具名称 | 功能描述 |
|---------|---------|
| `IsAnyTable` | 检查表是否存在于当前数据库 |
| `IsAnyColumn` | 检查列是否存在于指定表 |
| `IsPrimaryKey` | 检查指定列是否参与主键定义 |
| `IsIdentity` | 检查指定列是否配置为自增列 |
| `IsAnyConstraint` | 检查指定约束名称是否存在（唯一、外键或检查约束） |

### 表操作

| 工具名称 | 功能描述 |
|---------|---------|
| `DropTable` | 立即删除指定表，移除结构和数据 |
| `TruncateTable` | 对表执行 TRUNCATE，删除所有行但保留架构 |
| `BackupTable` | 复制表到新表名，包括架构和当前数据 |
| `RenameTable` | 重命名表 |

### 列操作

| 工具名称 | 功能描述 |
|---------|---------|
| `AddColumn` | 向表添加列，使用 JSON 描述列定义（列名、数据类型、长度、可空等） |
| `UpdateColumn` | 修改现有列，使用 JSON 指定新的列定义 |
| `DropColumn` | 从表中删除指定列 |
| `RenameColumn` | 重命名表中的列 |

### 约束和索引操作

| 工具名称 | 功能描述 |
|---------|---------|
| `AddPrimaryKey` | 为表的指定列创建主键约束 |
| `DropConstraint` | 删除指定约束（主键、唯一索引、外键等） |
| `CreateIndex` | 为表的指定列创建索引，支持唯一索引 |
| `IsAnyIndex` | 检查索引是否存在 |
| `GetIndexList` | 列出表上定义的所有索引（名称和属性） |

### 默认值和注释

| 工具名称 | 功能描述 |
|---------|---------|
| `AddDefaultValue` | 为表的指定列设置默认值 |
| `AddTableRemark` | 为表添加描述/注释 |
| `IsAnyTableRemark` | 检查表是否已有存储的注释 |
| `DeleteTableRemark` | 删除表的注释/描述 |
| `AddColumnRemark` | 为表的指定列添加描述/注释 |
| `DeleteColumnRemark` | 删除列的存储描述 |

### 存储过程、函数、视图操作

| 工具名称 | 功能描述 |
|---------|---------|
| `GetProcList` | 列出当前数据库中的所有存储过程名称 |
| `GetFuncList` | 列出所有数据库函数名称 |
| `DropView` | 删除指定视图定义 |
| `DropFunc` | 删除数据库函数 |
| `DropProc` | 删除存储过程 |

### 其他工具

| 工具名称 | 功能描述 |
|---------|---------|
| `GetDbTypes` | 返回当前 SqlSugar 构建支持的 DbType 值 |
| `GetTriggerNames` | 列出表上定义的所有触发器 |
| `GetTableSchema` | 返回表的综合架构文档，包括列、主键、自增列和索引 |

---

## Excel 导出工具 (ExcelExportTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `ExportQueryToExcel` | 将 SQL 查询结果导出到 Excel 文件，支持格式化选项（自动筛选、自动调整列宽、冻结标题行等） |
| `ExportTableToExcel` | 将整个表数据导出到 Excel 文件，支持 WHERE 子句过滤和架构信息工作表 |
| `ExportMultipleQueriesToExcel` | 将多个 SQL 查询导出到同一 Excel 文件的不同工作表，支持汇总工作表 |

---

## 文档生成工具 (DocumentationTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `GenerateDatabaseDocumentation` | 一步生成数据库文档，包含表/列/索引/触发器/视图；支持 markdown/json；输出可选 content/base64/path（可指定 filePath）；支持按表名过滤 |

---

## 工具统计

- **连接管理工具**: 9 个
- **查询工具**: 13 个
- **命令执行工具**: 9 个
- **架构管理工具**: 42 个
- **Excel 导出工具**: 3 个
- **文档生成工具**: 1 个

**总计**: 77 个工具

---

## 使用说明

1. 所有工具都通过 MCP (Model Context Protocol) 协议暴露
2. 工具支持 JSON 参数传递，便于复杂数据结构的处理
3. 危险操作（如 DROP、TRUNCATE）会进行检测和验证
4. 支持多数据库类型（MySQL、SQL Server、PostgreSQL、Oracle 等）
5. 所有工具都包含完整的错误处理和日志记录

---

## 相关文档

- [README.md](README.md) - 项目概述和快速开始
- [AGENTS.md](AGENTS.md) - 开发指南和规范
- [DatabaseSetting/](DatabaseSetting/) - 各数据库的配置说明
