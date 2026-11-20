# 清理非配置兼容层

范围：本地未推送的“优化1”至“优化6”和当前工作区。用户已允许破坏工具接口，不保留旧调用包装；直接实施，不改写提交历史。

不变量：保留配置字段别名、配置热更新/重命名/状态迁移、执行策略配置；保留驱动差异处理、权限检查、取消传播、精确数值、独占租约及准入身份。

- [x] 工厂仅维护准入身份和独占租约，移除共享 SqlSugarScope、旧 CreateClient 和默认接口兜底。
- [x] 参数统一为 DatabaseParameter 数组；去掉旧 JSON 字符串、名称式输出参数和宽松批命令解析。
- [x] query_database 作为通用查询入口；移除重复 sql_query / sql_query_with_in_parameter；合并存储过程入口。
- [x] 多结果与批查询统一 reader 路径，移除为旧返回行为保留的 DataSet/无限制分支。显式执行配置仍生效。
- [x] 同步 CLI/Web 元数据、工具说明、脚本与测试，删除仅验证旧接口的用例，保留行为覆盖。
- [x] 验证双框架单元测试、三库集成、打包真实 stdio 和 CLI 工具清单。

验收结果：net9/net10 各 357 项常规测试、38 项三库集成测试通过；最后的 CLI 绑定清理补跑各 49 项通过。包安装/stdio 验证通过；CLI 清单覆盖 56/56（SQLite 43 个成功、13 个不支持操作）。具体证据见 [清理记录](../Doc/compatibility-cleanup.md)。

回滚：如需回滚，只撤销本轮工作区 diff；不 reset/rebase 已有提交。旧客户端须重新发现工具 schema；配置文件无需迁移。框架目标和依赖版本不属于本轮接口清理范围。

验证重点：租约隔离与释放、配置刷新下的排队/重命名、typed NULL/精确数值/IN/输出参数、取消后连接复用、批操作部分成功与未执行项、权限和 JSON 字节预算、旧格式拒绝与当前 MCP schema。

参考：SqlSugar ADO https://www.donet5.com/Home/Doc?typeId=1198 、生命周期 https://www.donet5.com/Home/Doc?typeId=1224 、MCP C# SDK 2.4.0 tools https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.4.0/docs/concepts/tools/tools.md 。
