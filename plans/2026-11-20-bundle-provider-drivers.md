# 补齐四类 SqlSugar 驱动

用户已明确要求补齐 HG、GBase、TDengine、DuckDB 驱动。本轮直接实施，不使用子代理，保留当前暂存内容和 ORM 修复。

1. 对照 SqlSugar 官方文档和 NuGet 元数据选择当前稳定扩展包，确认传递依赖、原生库与版本兼容。
2. 添加 PackageReference，沿用集中 CustomAssemblies 注册与缺包预检；更新预设和发布说明，区分程序集可用与环境要求。
3. 将四库缺包断言改为实际驱动构造回归；DuckDB 验证本地建表、参数化写入、查询和元数据；发布测试验证所有扩展并模拟单独 DLL 缺失后的隔离行为。
4. 运行 net9/net10 回归、依赖审计、临时 NuGet 安装及 MCP stdio 验证；能获得匹配 SDK 的数据库进行隔离实库测试。
5. 记录已验证范围及仍需部署方安装的 SDK。无需改变数据库数据或系统全局 SDK；回退仅撤销本轮补丁及新增依赖，不重置其他更改。

补充：发现双框架包 299.3 MiB 超过 NuGet.org 上限，用户已选择仅发布 .NET 10 工具包。发布与包测试使用 `-p:TargetFrameworks=net10.0`，保留源码双框架及全部平台资产，增加包大小和运行时布局校验。

官方依据：[HG](https://www.donet5.com/Doc/1/2436)、[GBase](https://www.donet5.com/Doc/1/2440)、[TDengine](https://www.donet5.com/Doc/1/2566)、[DuckDB](https://www.donet5.com/Doc/1/2647)。
