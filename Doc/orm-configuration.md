# ORM 配置与支持范围

本页是当前 MCP 的配置契约。SqlSugar 支持某项 ORM 功能不代表本服务提供了相应工具。官方依据：[配置](https://www.donet5.com/Doc/1/2350)、[连接池](https://www.donet5.com/Doc/1/2362)、[元数据](https://www.donet5.com/Doc/1/1203)、[GaussDB](https://www.donet5.com/Doc/1/2438)、[金仓](https://www.donet5.com/Doc/1/2368)。

## 连接与驱动

- 每次操作独占 SqlSugarClient，IsAutoCloseConnection=true；物理连接由驱动池复用。热更新只改变后续租约。
- HG、GBase、TDengine、DuckDB 的 SqlSugar 扩展和 .NET 驱动已随包安装，启动时统一注册 CustomAssemblies。DuckDB 同时包含支持平台的原生库；GBase 仍需目标主机安装 ClientSDK/ODBC，TDengine 仍需匹配版本的原生客户端。版本和部署条件见[驱动清单](provider-drivers.md)。
- 预设 available 表示扩展程序集可加载，不证明原生 SDK、网络或认证正常。发布文件缺失时仍显示 unavailable，并在连接前返回 UnsupportedCapability；配置校验产生 warning，其他连接仍可使用。
- MongoDB 六个集合工具使用 SqlSugar.MongoDbCore 内置 ADO，共用 SqlSugar 客户端租约；输入支持 Extended JSON v2，输出为 provider 转换后的 JSON，不承诺 BSON 无损往返。读取先限制服务器行数，物化后检查响应预算；探活使用内置 aggregate。通用 SQL/Schema 工具仍返回 unsupported。批量写权限、取消和类型转换边界见[集合契约](mongodb-collections.md)。
- DB2/HANA 不在当前发布支持范围。ODBC/原生 SDK 的安装和平台位数仍须在部署目标检查。
- config validate/doctor 按实际驱动使用原生 builder 检查 MySQL、PostgreSQL/QuestDB、SQLServer、SQLite、Oracle、Kdbndp、Dm、Oscar、HG、GaussDBNative、MongoDB；其他 provider 检查通用连接串语法。离线检查不证明网络、认证或数据库功能正确。
- 高级 TLS、证书、Schema、Mongo 副本集等配置使用 provider 支持的原始连接字符串；当前 Mongo provider 明确拒绝 mongodb+srv URI，需使用显式主机 mongodb URI。Web 向导仅覆盖预设字段，端口限定 1–65535。预设密码是示例，SQLServer TrustServerCertificate=True 只适用于本地开发，应为生产配置可信证书。

## 优化配置的真实含义

optimizationSettings 接受字符串、布尔和数字标量，写回时统一字符串。旧字段、类型别名及未知优化键保留。

| 状态 | 含义 |
| --- | --- |
| mapped | 已映射到 SqlSugar.ConnMoreSettings；查看 scope，不能推断原始 SQL 被改写 |
| advisory | 保留配置但不执行；池大小、SSL、Schema、字符集等应设置在连接串 |
| not-applicable | 没有相应 MCP 执行功能，如 BulkCopy、游标/原生数组、自动 Hints、悲观事务、batchSize |
| unknown | 未识别键，值隐藏，不宣称生效 |
| ignored-invalid | 诊断非有效快照时可见；正常配置入口拒绝已知选项的非法值 |

Gauss 的 nativeDriver 不改变驱动；dbType 选择驱动，GaussDBNative 的 databaseModel 选择 GaussDB/OpenGauss。isOpenGauss 作为旧配置模式别名保留，冲突时报错。

SQLite CodeFirst 开关仅用于 CodeFirst；本服务 DDL 使用 DbMaintenance。SQLServer NoLock 仅影响实体 Queryable，不影响原始 ADO/SqlQueryable 查询，也不代表一致性提升。PG ILIKE/大小写选项只影响相应 ORM 生成路径。

## 能力、缓存与执行

get_database_capabilities 分开返回 databaseType（配置产品）、actualProvider（SqlSugar 归一化驱动）、productVerification 和 capabilityVerification。TiDB/Vastbase 等可复用驱动的分页实现，但仍标记产品未验证。typedProcedureOutput 仅开放 MySQL/SQLServer；其他产品明确拒绝。

元数据工具使用官方无缓存 API。get_table_schema 的应用缓存按连接身份、配置版本和表隔离，写操作失效。get_primaries 返回列名数组，不包含约束名和主键顺序；视图列表返回 Name/Description/DbObjectType，不包含定义。

SqlSugar 5.1.4.221 的 SQLite 自增检测依赖 sqlite_master 建表文本包含大写 AUTOINCREMENT；小写建表文本可能未被识别。MCP 保留官方列元数据行为，不自行解析 DDL。分页依赖主键标志，不依赖自增标志。

同步元数据/DDL 同样受 DB_COMMAND_MAX_TIMEOUT_SECONDS 限制。Command Timeout 连接参数不等于 SqlSugar ADO 超时；同步过程/GO/元数据/DDL 不支持请求取消。响应字节预算控制返回数据，不承诺过程内存上限。

## 本轮验证

验证命令及最终结果见 [ORM 修复记录](orm-remediation-2026-11-20.md)及[驱动补齐记录](provider-drivers.md)。本地驱动构造、DuckDB 文件读写和服务器集成是不同层次的证据；没有将其外推为所有数据库生产验收。
