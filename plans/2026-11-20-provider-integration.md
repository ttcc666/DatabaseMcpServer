# SqlSugar 官方用法对齐与真实驱动回归

用户已授权本轮实施、创建 Docker 测试容器；全程不使用子代理。

## 范围与不变量

- SqlSugarCore 保持 5.1.4.216，修复传递依赖 MySqlConnector/Pkcs 的已知漏洞。
- 租约每次创建独立 SqlSugarClient，IsAutoCloseConnection=true，释放上下文后由驱动连接池复用物理连接。
- 保留公共接口、旧 SqlSugarScope 兼容入口、准入上限/队列、取消、配置切换/重命名快照语义。
- SqlSugar 存储过程、输出参数、GO、多结果集沿用官方 Ado API；不将同步调用包装为伪异步。
- 用户已有暂存内容保持不变，不触碰现有容器/数据库。

## 实施清单

1. 核验官方文档与安全公告，固定最小兼容修复版本。
2. 移除 ORM 空闲实例池，补充状态隔离/释放回归。
3. 在现有 xUnit 项目加入显式启用的 MySQL、PostgreSQL、SQL Server 集成测试。
4. 编写 Docker 生命周期脚本：隔离名称、127.0.0.1 随机端口、随机密码、截止时间、finally 清理。
5. 验证参数/类型、多结果集、执行中取消、超时、批量中断、存储过程输出及 SQL Server GO；按实际失败修复。
6. 运行 net9/net10 测试与 Release 构建、NuGet 审计，记录结果，接入 CI。

## 验证与回退

- 集成测试默认跳过；脚本启用时缺失连接配置必须失败，不能假通过。
- 取消用服务端活动查询确认已执行，再发出取消，并确认服务端停止与后续请求可用。
- 测试失败先缩小复现，保留 SqlSugar 官方 provider 语义。
- 回退仅撤销本轮明确的补丁；不 reset/restore 用户暂存内容。脚本仅删除本轮有唯一标识的容器和附属匿名卷。
- 不把无性能基准的架构简化称为已量化性能提升。

## 官方依据

- https://www.donet5.com/Home/Doc?typeId=1224 — SqlSugarClient 生命周期、多线程独立上下文。
- https://www.donet5.com/Home/Doc?typeId=1181 — 底层连接池、CommandTimeOut、SqlSugarScope 单例。
- https://www.donet5.com/Home/Doc?typeId=1231 — Ado.CancellationToken。
- https://www.donet5.com/Home/Doc?typeId=1198 — Ado、reader 释放、UseStoredProcedure、输出参数。
- https://www.donet5.com/Home/Doc?typeId=2412 — .NET Core MySQL 默认驱动。
- https://github.com/advisories/GHSA-473q-m89c-ghf8 — MySqlConnector >=2.6.1。
- https://github.com/advisories/GHSA-2fmv-rv2c-59x7 — Pkcs 8.0.2。
- https://github.com/advisories/GHSA-846f-f764-76mx — Pkcs 8.0.2。
