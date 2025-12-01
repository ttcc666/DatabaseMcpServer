# 华为 OpenGauss / GaussDB (.NET) - 页面抓取（typeId=2438）

- 原始页面： https://www.donet5.com/Home/Doc?typeId=2438
- 抓取日期： 2025-11-28

---

## 概要
整理自 donet5 的 GaussDB/OpenGauss 文档，包含两种连接方式（Npgsql 与 原生）、连接字符串示例、数据类型映射、schema 使用、以及原生驱动的创建与配置示例。

---

# 华为 OpenGauss / GaussDB (.NET)

## Npgsql 方式

- 优点：成熟稳定；缺点：无法使用 GaussDB 特有特性。
- 连接字符串示例（与 PostgreSQL 相似）：

```
PORT=5432;DATABASE=SqlSugar4xTest;HOST=localhost;PASSWORD=haosql;USER ID=postgres;No Reset On Close=true
```

## 原生方式

- 需要安装 `SqlSugar.GaussDBNativeCore` 与 `SqlSugarCore`。
- 创建 DB 时需设置 `DbType = SqlSugar.DbType.GaussDBNative`，并可通过 `MoreSettings.DatabaseModel` 指定 `OpenGauss` 或 `GaussDBNative`。

---

> 注：已保存为 `donet5_gaussdb.md`。
