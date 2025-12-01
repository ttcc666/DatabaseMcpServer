# 人大金仓 (Kdbndp) .NET 操作 - 页面抓取（typeId=2368）

- 原始页面： https://www.donet5.com/Home/Doc?typeId=2368
- 抓取日期： 2025-11-28

---

## 概要
整理自 donet5 的“人大金仓 .NET 操作数据库”页面，包含版本/模式（Oracle/MySql/PostgreSQL/SqlServer）配置示例、表名大小写策略、游标与特殊类型 (JSON/Geometry) 支持、驱动兼容性与常见问题。

---

# 人大金仓 .NET 操作数据库

SqlSugar 与人大金仓官方深度整合，文档包含多模式示例（R6/R3 等）、配置示例、游标参数、数组与 Json 支持以及 Geometry/Postgis 的接入说明。

## NuGet 安装

```
SqlSugarCore
```

## 常用示例

- 查看安装的模式：`show database_mode;` 或 `SELECT version();`
- 连接示例（Oracle 模式）：

```
SqlSugarClient db = new SqlSugarClient(new ConnectionConfig() { DbType = DbType.Kdbndp, ConnectionString ="Server=127.0.0.1;Port=54321;UID=SYSTEM;PWD=system;database=SQLSUGAR4XTEST1", IsAutoCloseConnection = true, MoreSettings=new ConnMoreSettings() { DatabaseModel= DbType.Oracle } })
```

---

> 注：已保存为 `donet5_kdbndp.md`。如需我把该文件与之前 `donet5_type2368.md` 合并或删除重复，请告诉我。
