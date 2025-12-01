# QuestDb (.NET 时序数据库) - 页面抓取（typeId=2434）

- 原始页面： https://www.donet5.com/Home/Doc?typeId=2434
- 抓取日期： 2025-11-28

---

## 概要
整理自 donet5 的 QuestDb 文档，包含 QuestDb 的特点（高性能时序数据库）、连接字符串、分表/分区、symbol 类型限制、SampleBy/LastOn 等时序特性、并发与大数据导入建议等。

---

# 时序数据库 QuestDb

## 连接字符串

```
host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;
```

## 特性与注意点

- symbol 类型适合高重复率字段，去重后应小于 ~60k；
- 不支持删除单条记录（需 truncatetable 或删除分区）；
- 插入后短时间内可能查询不到（WAL 异步写入）；

---

> 注：已保存为 `donet5_questdb.md`。
