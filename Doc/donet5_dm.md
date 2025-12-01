# 达梦数据库 .NET 操作 - 页面抓取（typeId=1229）

- 原始页面： https://www.donet5.com/Home/Doc?typeId=1229
- 抓取日期： 2025-11-28

---

## 概要
本文档整理自 donet5 的“达梦数据库 .NET 操作”页面，包含连接字符串、表命名模式（大写/驼峰）、达梦特性、Docker 注意、schema 支持、Clob/Text 用法、常见错误与优化建议等。

---

# 达梦数据库 .NET 操作

SqlSugar 在 .NET 中对达梦数据库提供了良好兼容性，文档包含示例连接字符串、表名大小写处理、Docker 兼容模式设置与常见问题。

## NuGet 安装

```
SqlSugarCore
```

## 表模式（大写/小写）

- 默认大写；若使用小写表名可配置 `IsAutoToUpper=false`。

## 达梦连接字符串示例

```
// 老版本
PORT=5236;DATABASE=DAMENG;HOST=localhost;PASSWORD=SYSDBA;USER ID=SYSDBA

// 新版本
Server=localhost;User Id=SYSDBA;PWD=SYSDBA;DATABASE=新DB

// 带 Schema 示例
Server=153.101.101:5236;User Id=SYSDBA;PWD=123456;SCHEMA=myshcema;DATABASE=DAMENG
```

## Docker 注意

Docker 安装可能默认使用 MySQL 模式，需通过 `MoreSettings.DatabaseModel = SqlSugar.DbType.MySql` 来兼容分页等行为。

## Clob / Text 用法与驱动问题

- 使用 `[SugarColumn(SqlParameterDbType = typeof(NClobPropertyConvert))]`。
- 若出现插入空白问题，升级驱动 `SqlSugarCore.Dm 1.3.0+` 可解决。

## 常见错误（节选）

- .NET Framework 下 DLL 不兼容：升级 SqlSugar。
- 大文件 Clob/Blob 问题：安装最新 `SqlSugarCore.Dm` 包。
- 存储过程参数顺序：参数顺序需一致。

---

## 原始页面底部信息
2016 © donet5.com  Apache Licence 2.0

苏ICP备2020070057号
