# DatabaseMcpServer Tools 索引

本文档按照 `Tools/` 目录下的实现，汇总 DatabaseMcpServer 已暴露的 MCP 工具及功能。

## 目录

- [连接管理 (ConnectionTools)](#连接管理-connectiontools)
- [查询 (QueryTools)](#查询-querytools)
- [命令执行 (CommandTools)](#命令执行-commandtools)
- [架构管理 (SchemaTools)](#架构管理-schematools)
- [Excel 导出 (ExcelExportTools)](#excel-导出-excelexporttools)
- [文档生成 (DocumentationTools)](#文档生成-documentationtools)

---

## 连接管理 (ConnectionTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `TestConnection` | 在当前活动连接上运行 `SELECT 1`，返回连接状态、当前数据库和数据库类型 |
| `TestConnectionByName` | 使用指定连接名创建连接并测试健康度 |
| `GetDatabaseConfig` | 汇总当前配置：连接名、描述、数据库类型、掩码连接字符串、模式元数据 |
| `ValidateConfiguration` | 验证环境变量或配置文件能否生成可用连接 |
| `ListDatabases` | 列出所有已配置连接（名称、类型、描述、是否默认、是否当前） |
| `SwitchDatabase` | 切换活动连接到指定名称，并返回切换前后信息 |
| `GetCurrentDatabase` | 返回当前活动的连接名与数据库类型 |
| `HealthCheck` | 对全部已配置连接执行健康检查，含响应耗时与错误信息 |
| `TestConnectionWithRetry` | 带指数退避的连接测试，最多重试指定次数 |

---

## 查询 (QueryTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `SqlQuery` | 执行只读 SQL，检测危险操作并支持 JSON 参数，返回行数与数据 |
| `SqlQuerySingle` | 执行只读 SQL，仅返回首行（或 null） |
| `GetDataSetAll` | 执行包含多个查询的 SQL，返回各结果集及行数 |
| `GetScalar` | 返回首行首列值，适用于 COUNT/SUM 等标量查询 |
| `SqlQueryWithInParameter` | 绑定 JSON 数组到 IN 参数并可附加其他参数，安全执行 IN 查询 |

---

## 命令执行 (CommandTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `ExecuteCommand` | 执行 INSERT/UPDATE/DELETE，含危险操作检测与可选 JSON 参数，返回受影响行数 |
| `CallStoredProcedure` | 调用存储过程，可选参数，返回行数与结果 |
| `CallStoredProcedureWithOutput` | 调用存储过程并收集输出参数与结果集 |
| `ExecuteCommandWithGo` | 执行包含 GO 批处理的 SQL Server 脚本并汇总受影响行数 |
| `BatchExecuteCommands` | 在同一连接上批量执行 SQL 数组，可为每条提供独立参数，逐条报告结果 |

---

## 架构管理 (SchemaTools)

**数据库信息**

| 工具名称 | 功能描述 |
|---------|---------|
| `GetDataBaseList` | 返回实例可见的数据库列表 |
| `GetViewInfoList` | 获取视图的名称、定义及架构信息 |
| `GetTableInfoList` | 拉取表的基本信息（名称、描述、创建时间） |
| `GetColumnInfosByTableName` | 获取指定表的列信息（类型、长度、可空等） |
| `GetIsIdentities` | 返回表的自增列 |
| `GetPrimaries` | 返回表的主键信息（约束名、列、序号） |

**存在性检查**

| 工具名称 | 功能描述 |
|---------|---------|
| `IsAnyTable` | 检查表是否存在 |
| `IsAnyColumn` | 检查列是否存在于指定表 |
| `IsAnyConstraint` | 检查约束（唯一、外键、检查等）是否存在 |

**表操作**

| 工具名称 | 功能描述 |
|---------|---------|
| `DropTable` | 删除指定表（结构与数据） |
| `TruncateTable` | TRUNCATE 表，清空数据保留架构 |
| `BackupTable` | 复制表到新表名（含架构与现有数据） |
| `RenameTable` | 重命名表 |

**列操作**

| 工具名称 | 功能描述 |
|---------|---------|
| `AddColumn` | 使用 JSON 列定义添加列，自动根据类型处理长度/小数位 |
| `UpdateColumn` | 使用 JSON 定义修改列，自动处理长度/小数位 |
| `DropColumn` | 删除指定列 |
| `RenameColumn` | 重命名列 |

**约束与索引**

| 工具名称 | 功能描述 |
|---------|---------|
| `AddPrimaryKey` | 为指定列创建主键约束 |
| `DropConstraint` | 删除指定约束（主键、唯一、外键等） |
| `CreateIndex` | 为列创建索引，可设唯一 |
| `GetIndexList` | 列出表上所有索引及属性 |

**默认值与注释**

| 工具名称 | 功能描述 |
|---------|---------|
| `AddDefaultValue` | 为列设置默认值 |
| `AddTableRemark` | 为表添加描述/注释 |
| `IsAnyTableRemark` | 检查表是否已有注释 |
| `DeleteTableRemark` | 删除表的注释 |
| `AddColumnRemark` | 为列添加描述（SQL Server 使用扩展属性，其余使用 SqlSugar） |
| `DeleteColumnRemark` | 删除列的描述（SQL Server 使用扩展属性，其余使用 SqlSugar） |

**存储过程/函数/视图**

| 工具名称 | 功能描述 |
|---------|---------|
| `GetProcList` | 列出所有存储过程名称 |
| `GetFuncList` | 列出所有函数名称 |
| `DropView` | 删除指定视图 |
| `DropFunc` | 删除函数 |
| `DropProc` | 删除存储过程 |

**其他**

| 工具名称 | 功能描述 |
|---------|---------|
| `GetTriggerNames` | 列出表上定义的触发器 |
| `GetTableSchema` | 返回表的汇总架构（列、主键、自增列、索引） |

---

## Excel 导出 (ExcelExportTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `ExportQueryToExcel` | 将查询结果导出为 Excel；支持自动筛选、列宽、冻结表头、base64 或路径返回 |
| `ExportTableToExcel` | 导出整表数据（可带 WHERE），可选附带 Schema 工作表，支持 base64 或路径返回 |
| `ExportMultipleQueriesToExcel` | 多查询多工作表导出，可选汇总页，支持 base64 或路径返回 |

---

## 文档生成 (DocumentationTools)

| 工具名称 | 功能描述 |
|---------|---------|
| `GenerateDatabaseDocumentation` | 一次生成数据库文档（表/列/索引/触发器/视图），支持 markdown/json，content/base64/path 返回并可按表名过滤 |

---

## 工具统计

- **连接管理**: 9 个
- **查询**: 5 个
- **命令执行**: 5 个
- **架构管理**: 34 个
- **Excel 导出**: 3 个
- **文档生成**: 1 个

**总计**: 57 个工具

---

## 使用说明

1. 所有工具通过 MCP (Model Context Protocol) 暴露。
2. 支持 JSON 形式传递参数，便于复杂对象输入。
3. 危险操作（如 DROP、TRUNCATE）均有检测或需使用专用 Schema 工具执行。
4. 支持多种数据库类型（MySQL、SQL Server、PostgreSQL、Oracle、SQLite 等）。
5. 所有工具具备异常处理与日志记录，建议在调用侧保留错误输出以便排查。

---

## 相关文档

- [README.md](README.md) - 项目概述与快速开始
- [AGENTS.md](AGENTS.md) - 开发指南与规范
- [DatabaseSetting/](DatabaseSetting/) - 数据库配置示例
