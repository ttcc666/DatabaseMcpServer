# SqlSugar 接入与数据库配置审查

后续实施记录：[ORM 审计修复](orm-remediation-2026-11-20.md)、[四类驱动补齐](provider-drivers.md)。以下保留修复前的审计证据；当前行为以 [配置约定](orm-configuration.md) 为准。

MongoDB 后续实现见[集合查询/写入契约](mongodb-collections.md)。

日期：2026-11-20。范围：当前暂存代码（HEAD `5452fe7` 及上一轮兼容层清理）、实际 NuGet 依赖、24 个 CLI/Web 预设、数据库策略、MCP 工具和 `DatabaseSetting/` 文档。仅新增本报告，未修改业务代码、依赖或暂存内容。

## 结论与证据边界

核心生命周期和 ADO 使用方向符合 SqlSugar 官方规范；数据库接入范围、元数据缓存、配置语义还不能认定为完整正确。最优先的问题是跨连接元数据串用，以及对缺少驱动的数据库仍提供可选预设。

本轮直接读取 31 篇 SqlSugar 官方文档，并以项目实际安装的 `SqlSugarCore 5.1.4.221` 和扩展包验证。官方网页会更新；涉及当前版本行为的结论以实际程序集探针为准。Context7 查询失败后使用官方站点，不依赖搜索摘要。

- 24 个预设逐项构造客户端、ADO provider 和连接对象，**不调用远程 Open/CheckConnection**：19 个完成基础构造，5 个失败。基础构造不证明网络、认证、服务端版本或 CRUD 正确。
- HG、GBase、TDengine、DuckDB 另以独立进程确认缺包，避免 SqlSugar 静态状态相互影响。
- 两个临时 SQLite 文件验证元数据隔离和 DDL 后刷新；另验证配置反序列化、GaussDB 驱动选择、生成 SQL、能力诊断。
- 现有相关测试 net10.0：**60 passed、0 failed、0 skipped**。本轮未重新运行三库 Docker；上一轮 MySQL/PostgreSQL/SQL Server 集成通过的证据见 [兼容清理记录](compatibility-cleanup.md)，不外推到其他数据库。

## 已符合官方用法的部分

| 检查项 | 当前实现 | 判断 |
| --- | --- | --- |
| 客户端生命周期 | `SqlSugarClientFactory.CreateLease` 每次 new 客户端，独占租约、结束 Dispose，`IsAutoCloseConnection=true` | 符合官方 SqlSugarClient 模式；无需改回共享 SqlSugarScope，也不需要在公共方法补 CopyNew |
| 配置更新 | 新请求取得配置快照，新建客户端；旧请求继续使用原实例 | 符合官方禁止在已有实例上修改 DbType/ConnectionString 的要求 |
| 连接复用 | 驱动池复用物理连接，批操作 `using (Ado.OpenAlways())` | 符合官方 ADO/连接池用法；应用准入数量和驱动池容量是不同概念 |
| 参数 | `SugarParameter`，显式类型/方向/size，IN 数组由 SqlSugar 绑定 | 基础用法正确；Oracle/Kdbndp 游标、JSON、原生数组等未完整接入 |
| 异步读取 | await ADO 调用，释放 reader，再释放租约；设置并恢复 Ado.CancellationToken/CommandTimeOut | 已验证四库路径方向正确；其他 provider 的真实取消仍需实库验证 |
| 新结构查询 | `get_table_schema` 使用 `GetColumnInfosByTableName(table,false)`，应用缓存含连接身份和配置版本 | 这条路径正确；旧元数据工具没有全部采用同一规则 |
| 分页 | SqlQueryable + 外部 OrderBy + 官方分页 API，不隐式 COUNT | 四库有回归证据；大页仍先物化 DataTable 再做字节限制，预算不等于内存上限 |

官方依据：[线程安全](https://www.donet5.com/Doc/1/1224)、[生命周期](https://www.donet5.com/Doc/1/2317)、[连接池](https://www.donet5.com/Doc/1/2362)、[修改配置](https://www.donet5.com/Doc/1/2350)、[原生 SQL/过程/reader](https://www.donet5.com/Doc/1/1198)。

## 问题清单（按修复优先级）

### P1：部分元数据工具跨连接串用缓存，DDL 后也可能陈旧

位置：`Tools/Management/SchemaTools.cs:37,53,59,73`；`Services/SqlSugarClientFactory.cs:110`。

`GetViewInfoList()`、`GetPrimaries()`、`GetIsIdentities()`、`IsAnyColumn()` 仍使用 SqlSugar 默认缓存。当前工厂没有设置稳定且互异的 ConfigId。实际版本的基础缓存键含数据库类型、`Ado.Connection.Database`、表名、部分路径的 ConfigId；不包含完整服务器/连接身份，SQLite 列缓存路径甚至不含 ConfigId。它也不受项目 `DatabaseMetadataCache.Invalidate` 控制。

两份 SQLite 文件，同名 `cache_probe` 表，分别以 `first_id` 和 `second_id` 为主键，实际输出：

```text
first_pk=first_id
second_pk=first_id
second_fresh_pk=second_id
views_before=0
views_after=0; fresh=1
old_col_before=True
old_col_after=True; fresh=False
```

最后一项直接调用 `DbMaintenance.DropColumn`，仍无法让默认列存在性缓存刷新。影响：agent 获得错误主键/列/视图信息，后续 SQL 或结构修改会针对错误结构。

建议：所有动态元数据读取统一走官方无缓存 API；主键/自增直接由新鲜列信息派生；需要缓存时只使用项目的身份+版本+TTL 缓存。仅增加 ConfigId 不足以解决所有 provider 缓存和 DDL 失效问题。`IsAutoRemoveDataCache` 管理二级数据缓存，不能替代元数据失效。

依据：[库表管理的 isCache 参数](https://www.donet5.com/Doc/1/1203)、[配置/二级数据缓存](https://www.donet5.com/Doc/1/2350)，及当前 `DbMaintenanceProvider` / `SqliteDbMaintenance` 实际程序集。

### P1：四个数据库预设缺少发布依赖，失败还可能影响后续扩展 provider

位置：`Cli/CliConfigPresetCatalog.cs:29,31,33,34`；`DatabaseMcpServer.csproj:50`；`Services/SqlSugarProviderWarmup.cs:10`。

| 预设 | 独立进程实际错误 | 官方要求 |
| --- | --- | --- |
| HG | `Not Found SqlSugar.HGCore.dll` | HG provider、对应驱动 |
| GBase | `Not Found SqlSugar.GBaseCore.dll` | SqlSugar.GBaseCore；另外需要平台 ODBC/客户端 SDK |
| TDengine | `Not Found SqlSugar.TDengineCore.dll` | SqlSugar.TDengineCore；匹配版本和架构的原生依赖 |
| DuckDB | `Not Found SqlSugar.DuckDBCore.dll` | SqlSugar.DuckDBCore 及嵌入式驱动 |

更进一步，同进程先尝试 HG，再创建已有依赖的 MongoDB，MongoDB 也报 `Not Found SqlSugar.HGCore.dll`；MongoDB 在独立进程或失败前可正常构造。当前 SqlSugar 扩展程序集发现使用静态列表，缺失项会影响后续查找，不能把这个错误简单当成“该连接失败后继续即可”。

建议：建立 provider 可用性目录，在进入 SqlSugar 工厂前确认发布依赖；已承诺支持的数据库补齐包及平台验证，其他明确显示“未安装/不可用”。不要只增加字符串类型映射。官方 CustomAssemblies 注册应在启动集中完成；当前仅 Assembly.Load 的预热在普通发布下已能工作，但不等于官方显式注册，也未证明裁剪/单文件支持。

依据：[HG](https://www.donet5.com/Doc/1/2436)、[GBase](https://www.donet5.com/Doc/1/2440)、[TDengine](https://www.donet5.com/Doc/1/2566)、[DuckDB](https://www.donet5.com/Doc/1/2647)。

### P1：金仓内置连接串无法被当前驱动解析

位置：`Cli/CliConfigPresetCatalog.cs:24`，Web 字段又由该预设推导。

`Server=localhost;Port=54321;Database=crm;User=SYSTEM;...` 在构造 Kdbndp 连接时抛出 `Keyword not supported: user`。仅把 `User` 改成 `User ID`，同一驱动基础构造成功。官方示例使用 USER ID。

建议：修正预设及依赖它的文档/向导，增加所有发布预设的离线驱动构造验证。此问题无需等待真实金仓服务器即可阻止回归。

### P1：MongoDB 依赖可加载，但数据库 MCP 的主要查询契约不适用

位置：`Services/DatabaseQueryService.cs:13`、`Helpers/SqlSafetyGuard.cs:12`、`Helpers/DatabaseCapabilities.cs:8`。

SqlSugar 官方 Mongo 原生命令是 `db.collection.find(...)` 等，或 Queryable 表达式。当前所有通用只读查询先要求 SELECT/WITH/SHOW 等 SQL 前缀，Mongo 原生命令被拒绝；表分页又只允许 SQLite/MySQL/PostgreSQL/SQL Server。预设描述的“CRUD/分页/简单联表”是 SqlSugar 库能力，并非当前 MCP 已交付能力。

建议：若继续承诺 MongoDB，需要 provider 专用、结构化的集合查询/写入/分页契约以及对应权限验证；不能直接放宽 SQL 安全校验。尚未实现前，把相关能力明确标为未接入。

依据：[MongoDB 官方 CRUD/原生命令](https://www.donet5.com/Doc/1/2651)。

### P1：部分完整配置示例无法反序列化

位置：`Models/DatabaseConnection.cs:73`、`DatabaseSetting/GaussDB.md:244`、`DatabaseSetting/Kdbndp.md`。

`OptimizationSettings` 是 `Dictionary<string,string>`，加载选项没有布尔/数值转字符串转换器。文档却出现 `"nativeDriver": true`、`"batchSize": 5000`。实际报错位置为 `$.databases[0].optimizationSettings.nativeDriver`。

扫描可解析的 JSON 片段时，在 GaussDB/Kdbndp 两份文档中发现 22 个此类值（未把包含注释、省略号或 URL 的片段计入统计）。建议先统一为可运行示例；以后若改成类型化配置，应保留用户要求的配置迁移兼容。

### P2：多项“已启用”的配置只记录日志

位置：`Strategies/DBSetting/KdbndpOptimizationStrategy.cs:94`、`GaussDbOptimizationStrategy.cs:95`、`DmOptimizationStrategy.cs:57` 及 MySQL 系策略。

| 配置 | 当前实际行为 | 正确归属 |
| --- | --- | --- |
| maxPoolSize、disablePooling、enableSsl、charset、allowUserVariables | 多数策略仅日志/提示 | 原生驱动连接串；不得声称被 optimizationSettings 应用 |
| 金仓 enableCursor | 仅输出“已启用” | 官方要求连接串 `CursorAsDataRead=True` 和 `SugarParameter.IsRefCursor=true`；当前输出绑定还禁止 Kdbndp |
| 金仓 enableJson、enableArray、enableGeometry | 仅日志 | 参数/实体类型映射；普通 IN 数组不能等同原生数组参数 |
| Gauss typeMapping、batchSize | 仅日志 | 类型适配及真实批处理路径；当前无相应实现 |
| DM/Kdb/Gauss schema | 仅日志 | 驱动 SCHEMA / Search Path 或明确 SQL 对象限定 |
| TiDB pessimisticTxn、enableHints | 仅日志 | 会话/事务设置，SQL hints；不会因设置该键自动生效 |
| enableBulkCopy | 仅日志 | 当前 MCP 没有 BulkCopy 工具，普通 batch 命令不是 BulkCopy |

建议：配置状态统一为实际映射、仅建议、不适用、无效；只对真正生效的项输出“已应用”。配置文件的旧键可保留并明确告知“不应用”，不能用兼容为由静默伪装成功。

### P2：GaussDB 驱动选择与数据库模式混淆

位置：`Strategies/DBSetting/GaussDbOptimizationStrategy.cs:35`；`DatabaseSetting/GaussDB.md:19,114`。

官方通过 `ConnectionConfig.DbType=GaussDB/OpenGauss/GaussDBNative` 选择驱动；`MoreSettings.DatabaseModel` 描述原生 provider 对应的数据库模式。当前 nativeDriver 改的是 DatabaseModel。

实际探针：

```text
dbType=GaussDB + nativeDriver=true  -> Npgsql.NpgsqlConnection
dbType=GaussDBNative + nativeDriver=false -> OpenGauss.NET.OpenGaussConnection
```

因此文档所称“nativeDriver=true 切换原生驱动”不成立。Gauss 文档的 Npgsql 示例还使用 `dbType=PostgreSQL`，会进入 PostgreSQL 策略，无法表达 Gauss 专有模式。

建议：dbType 作为驱动选择唯一来源，单独显式配置数据库模式，检查冲突；Gauss/OpenGauss Npgsql 示例使用官方专用 DbType。原生模式的 DatabaseModel 默认值与服务端版本对应关系需实库验证。

依据：[Gauss/OpenGauss 官方两种连接方式](https://www.donet5.com/Doc/1/2438)。

### P2：配置校验停留在结构层，向导能力也受预设字段限制

位置：`Helpers/DatabaseConfigurationValidator.cs:38`；`Web/CliConnectionStringProfileCatalog.cs:85`；`Web/CliConnectionStringBuilder.cs:17`。

错误连接串 `User=...`、`maxPoolSize=-1`、`mode=typo` 的配置，结构校验仍返回 `Errors=[]`。优化参数的值非法时通常静默使用默认值。Web 向导使用通用 DbConnectionStringBuilder，只校验字段名/整数/布尔形状，无法验证 provider 语义；非 Mongo 端口只要求整数，没有统一范围检查。

向导字段来自预设样例，因此 Oracle TCPS/钱包、PostgreSQL SSL Mode/Search Path、Mongo SRV/replicaSet/TLS/多主机等不能完整通过向导表达，需原始连接串模式。应标明这种边界，不要将支持原始串和向导完整支持混为一谈。

建议：共享 provider 描述目录，分开进行配置结构校验、驱动构造校验、可选真实连接诊断；所有入口返回一致结果，诊断不得泄露连接串。

### P1：能力诊断混淆数据库产品与实际驱动

位置：`Helpers/DatabaseCapabilities.cs:8,15,27,43`。

- SqlSugar 将部分兼容库的 DbType 归一化为 MySql/PostgreSQL。实测 TiDB 返回 `typedProcedureOutput=supported`，Vastbase 返回 `tablePagination/asyncQueryCancellation=supported`，但本项目没有对应产品的实库验收。不能把 MySQL/PostgreSQL 驱动验证当作这些产品的验证。
- Oracle、DM 等实际 provider 的 boundedMultipleResults/tablePagination 返回 `unverified`，对应工具却直接拒绝；应分别表达“工具可用性”和“验证程度”。
- SQLite enableDefaultValue/enableDescription 已应用到 ConnMoreSettings，但诊断返回 `unverified`、`effective=null`。
- Oracle/DM/Kdb/Gauss 等实际设置没有进入统一映射；仅 MySQL 的部分无效优化键被标注 advisory。
- 金仓原生数组/游标、Oracle 输出参数等是当前工具未接入，不能据 SqlSugar 支持就称为 MCP 支持。
- asyncQueryCancellation 的验证对象是查询路径，不能外推到同步过程、GO、DDL/元数据。

### P2：部分 ORM 设置与 MCP 实际执行路径无关

位置：`Strategies/DBSetting/SqlServerOptimizationStrategy.cs:24`、`SqliteOptimizationStrategy.cs:28`、`Tools/Management/SchemaTools.cs:93`。

SQL Server NoLock 在实体 Queryable 生成 SQL 中确实生效，但实际 `SqlQueryable("SELECT * ...").OrderBy(...).Skip/Take` 探针不含 WITH(NOLOCK)，原生 ADO 也不自动重写 SQL。不能把“启用 ORM NoLock”解释成所有 MCP 查询已应用。

SQLite 的 CodeFirst 默认值/备注/删除列开关针对 CodeFirst，当前 schema 工具调用 DbMaintenance。文档应按实际 API 说明范围，不能把 CodeFirst 开关称为当前工具的功能开关。PostgreSQL ILIKE、标识符大小写等也主要影响 ORM 生成 SQL，不会重写传入的 SQL 文本。

建议：区分 ORM 生成设置、驱动设置、MCP 执行策略。即使官方性能示例推荐 NoLock，也应明确其允许脏读的业务语义，不能替用户默认为一致性升级。

### P2：部分工具说明超出 SqlSugar 实际返回值

位置：`Tools/Management/SchemaTools.cs:36,56`。

`get_primaries` 描述承诺约束名、列、复合主键顺序，实际直接返回 `List<string>` 列名；`get_view_info_list` 描述承诺视图定义/Schema，实际返回 DbTableInfo 基础信息。应按实际响应改写工具说明，或实现明确的扩展 DTO。用户要求 agent 依据当前工具说明调用，更需要保证说明与响应一致。

### P2：同步工具的超时/取消覆盖不完整

位置：`Tools/McpToolBase.cs:78`；`Tools/Management/SchemaTools.cs`；`Tools/Command/CommandTools.cs:53,89`。

异步通用查询/命令设置了取消和超时；同步元数据/DDL 通过 WithClient 直接调用，没有统一进入 ResolveTimeout，因此服务端最大命令超时设置并非覆盖所有工具。过程和 GO 有可选超时，但没有取消令牌。当前所有基础 provider 探针的 Ado.CommandTimeOut 为 300；PostgreSQL 连接串的 `Command Timeout=30` 也不等于 SqlSugar 的有效命令超时。

建议：能力说明列明边界；先统一元数据/DDL 命令超时，再按 provider 支持逐步引入异步取消。不能只用 Task.Run 包装同步操作就宣称数据库已取消。

## 逐数据库核对表

“构造通过”只表示本轮不打开远程连接的检查通过。“四库验证”指上一轮真实数据库/SQLite 回归，详细范围见原记录。

| 数据库/DbType | 当前驱动与基础检查 | 配置完整性/后续要求 |
| --- | --- | --- |
| MySql | MySqlConnector 2.6.2；构造通过，四库验证 | disableNvarchar 有映射；其他优化键多为提示。默认 AllowLoadLocalInfile/Allow User Variables 不应解释成已实现 BulkCopy；按实际需求启用 |
| PostgreSQL | Npgsql 5.0.18；通过，四库验证 | lower/CodeFirst lower/ILike/identity 映射有效；TLS、Search Path 需原生串；须区分 Ado 超时与连接串超时 |
| SqlServer | Microsoft.Data.SqlClient 5.2.2；通过，四库验证 | ORM NoLock 不覆盖原生查询；预设 TrustServerCertificate=true 是跳过证书校验，应与生产模板分开 |
| Sqlite | Microsoft.Data.Sqlite 10.0.11；通过，SQLite 回归 | 修复元数据串缓存；CodeFirst 设置不代表 DbMaintenance 能力；文件路径/目录权限/WAL/Foreign Keys/Busy Timeout 需明确 |
| Oracle | Oracle.ManagedDataAccess.Core 23.8.0；通过 | 大小写/identity/maxParamLength 有真实映射；无实库证据；输出/游标未接入，专用分页/多结果被禁止；版本、service/SID、TCPS/钱包需补充 |
| MongoDb | MongoDB.Driver 3.11.1 + SqlSugar.MongoDbCore 5.1.4.280；通过 | 主要 MCP 查询契约不适用；SRV/副本集/TLS 需原始 URI；不能宣称 ORM 全部能力已开放 |
| ClickHouse | ClickHouse.Client 7.8.2 + 扩展 5.1.4.201；通过 | 配置 maxPoolSize 仅提示；无 BulkCopy 工具；事务不支持；TLS/压缩/超时及服务端引擎需验证 |
| Tidb | 实际 MySqlProvider/MySqlConnector；通过 | 专用类型会被 SqlSugar 转为 MySQL provider；Hints/悲观事务/池大小键不生效；四库能力检查可因此继承 MySQL 标记，不能当作 TiDB 实库验证 |
| OceanBase | MySqlProvider；通过 | 同上，兼容 MySQL 不等于所有能力已验证；租户/模式通过真实连接参数管理；关闭池应依据官方适用条件 |
| OceanBaseForOracle | SqlSugar.OceanBaseForOracleCore 5.1.4.205 + Odbc；通过 | 构造成功不检查本机是否安装 OceanBase ODBC 驱动；需验证位数/平台/租户格式/过程与大小写 |
| Dm | SqlSugarCore.Dm 8.8.1 / DmConnection；通过 | 大小写和 MySQL 分页模式映射与官方一致；schema、Clob、pool 键仅提示；SCHEMA 放驱动串，按服务器模式实测 |
| Kdbndp / kingbase | SqlSugarCore.Kdbndp 9.3.8.413；预设失败，User ID 修正候选通过 | 模式/大小写真实映射；默认 Oracle 模式需与服务器一致；游标/JSON/数组标志没有接入；完整示例类型错误 |
| GaussDBNative | 扩展 5.1.4.189，实际 OpenGauss.NET.OpenGaussConnection；通过 | nativeDriver 无法切换驱动；DatabaseModel、默认模式和服务端能力需明确；原生驱动与 Npgsql 的配置不能混为一谈 |
| OpenGauss | 实际 PostgreSQLProvider/Npgsql；通过 | No Reset On Close 与官方 Npgsql 示例一致；Search Path/认证要求需说明；数据库专用类型和标准 PG 的验证应分开 |
| GaussDB | 可解析、无独立预设；候选构造为 PostgreSQLProvider/Npgsql | 使用专用 dbType；nativeDriver=true 仍为 Npgsql；Npgsql/OpenGauss 模式不能靠日志选择 |
| PolarDB | MySqlProvider；通过 | 当前实现针对 MySQL 兼容版，不代表 PostgreSQL/Oracle 兼容版；多项优化只是日志；预设禁用池对应官方示例，需与实际版本核对 |
| Vastbase | PostgreSQLProvider/Npgsql；通过 | No Reset On Close 与官方一致；autoToLower 有映射，缺独立 CodeFirst 大小写配置；schema 放 searchpath |
| HG（瀚高） | 缺 SqlSugar.HGCore，失败 | 补依赖/驱动后验证 Search Path 和大小写；不要继续使用官方样例公网 IP 作为可执行默认地址 |
| GoldenDB | MySqlProvider；通过 | 关闭连接池与官方示例一致，但池建议不等于优化键已应用；需要真实连续读写验证 |
| GBase | 缺 SqlSugar.GBaseCore，失败 | 还需本机 ODBC/SDK、正确架构和locale；事务要求库启用 WITH LOG |
| Doris | MySqlProvider；通过 | 池配置参考官方特殊情况；DML/事务/元数据限制应单独验证，不能按 MySQL 全能力标记 |
| TDengine | 缺 SqlSugar.TDengineCore，失败 | 核对原生 SDK、版本/位数；时间精度、STable/Tag、写入路径没有专用 MCP 契约 |
| DuckDB | 缺 SqlSugar.DuckDBCore，失败 | 包和平台原生依赖补齐后验证本地文件、读写锁、复杂结果类型；不能仅增加 DbType |
| QuestDB | QuestDBProvider/Npgsql；通过 | ServerCompatibilityMode=NoTypeLoading 与官方示例一致；DELETE/事务/DDL 并非普通 PG 能力，需按独立 provider 描述 |
| Oscar（神通） | Oscar.Data.SqlClient 4.0.4；通过 | 当前 Data Source/User Id 别名可解析，不应误判为 Oracle 驱动；预设遗漏目标 Database/显式端口，而官方示例含 DATABASE/PORT，需补明确目标并实测 |
| DB2 / HANA | 运行时 ParseDbType 拒绝；无有效预设/工厂映射 | README 已标移除，但 NuGet Description 和 Doc/db.md 仍有旧清单；残留策略类不等于支持。官方页面仍提示源码接入，不能直接虚构包版本 |

**额外发现：别名会影响验证标签。** SqlSugar 将 TiDB/OceanBase/PolarDB/GoldenDB/Doris 转为 MySQL provider，把 GaussDB/OpenGauss/Vastbase 转为 PostgreSQL provider。当前能力函数读取 `Client.CurrentConnectionConfig.DbType`；已实测 TiDB/Vastbase 被标为四库 `supported`。应同时保留“配置的数据库产品”和“实际驱动/方言”，按产品及验证记录判断能力；不能只凭 ORM 内部归一化后的 DbType。

## 推荐实施顺序

1. **修正可复现的正确性问题**：统一元数据读取/缓存，阻断缺失 provider 进入 SqlSugar，修正金仓预设和不可解析示例。验收包括双连接同名表、DDL 后刷新、缺包后其他连接仍可用。
2. **建立单一 provider 目录**：数据库产品/别名、SqlSugar DbType、实际 provider、依赖、原生前提、配置字段和能力状态统一建模；CLI/Web/校验/能力工具/文档从同一来源读取。保留已有配置字段迁移规则。
3. **收敛配置语义**：真实 ConnMoreSettings 映射与驱动串设置分开，删除“只写日志却已启用”的表述；nativeDriver 不再伪装驱动切换；有效值/来源/适用执行路径可查询。
4. **逐类补验证再扩能力**：先 Oracle、DM、金仓、Gauss 和 ClickHouse；DuckDB可本地验证，其他库需隔离实例/必要客户端许可；MongoDB作为独立命令模型设计。未验证能力保留明确状态。

建议的验收矩阵应至少含：发布包 provider 构造、预设与文档 JSON 加载、目标库/Schema、NULL/Unicode/Decimal/日期时区/二进制、元数据隔离、取消和超时、连续请求与连接池、权限、分页与结果预算。按 provider 相关能力选择项目，不能把所有 ORM 功能强行塞入数据库 MCP。

## 本机复核命令与产物

仓库根目录，本轮实际执行：

```powershell
dotnet test 'tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj' -c 'Release' -f 'net10.0' --no-build --filter 'FullyQualifiedName~ClientLeaseTests|FullyQualifiedName~ExecutionConcurrencyTests|FullyQualifiedName~MetadataAndPagingTests|FullyQualifiedName~TypedParameterTests|FullyQualifiedName~DatabaseConnectionTests|FullyQualifiedName~ConfigurationEditingTests|FullyQualifiedName~SqlSugarClientFactoryTests'
dotnet run --project '.git/orm-audit/probe/Probe.csproj' -c 'Release'
dotnet '.git/orm-audit/probe/bin/Release/net10.0/Probe.dll' 'HG' 'MongoDb'
```

探针、下载页面和反编译片段在本机 `.git/orm-audit/`，不进入发布包或版本控制。`probe.log` 含基础构造/配置/SQLite 缓存证据，`provider-contamination.log` 含缺包影响后续 provider 的证据，`tests.log` 为 60 项测试结果。探针仅创建 `.git/orm-audit/cache-*/` 临时 SQLite 文件，不打开远程连接。

## 官方资料索引

- 核心：[线程安全](https://www.donet5.com/Doc/1/1224)、[生命周期](https://www.donet5.com/Doc/1/2317)、[ADO](https://www.donet5.com/Doc/1/1198)、[库表管理](https://www.donet5.com/Doc/1/1203)、[配置](https://www.donet5.com/Doc/1/2350)、[连接池](https://www.donet5.com/Doc/1/2362)、[异步](https://www.donet5.com/Doc/1/1231)。
- 常见关系库：[MySQL](https://www.donet5.com/Doc/1/1219)、[PostgreSQL](https://www.donet5.com/Doc/1/1221)、[SQL Server](https://www.donet5.com/Doc/1/1218)、[Oracle](https://www.donet5.com/Doc/1/1220)、[SQLite](https://www.donet5.com/Doc/1/1222)。
- 国产与兼容库：[达梦](https://www.donet5.com/Doc/1/1229)、[金仓](https://www.donet5.com/Doc/1/2368)、[Gauss/OpenGauss](https://www.donet5.com/Doc/1/2438)、[OceanBase](https://www.donet5.com/Doc/1/2564)、[TiDB](https://www.donet5.com/Doc/1/2565)、[PolarDB](https://www.donet5.com/Doc/1/2575)、[Vastbase](https://www.donet5.com/Doc/1/2570)、[HG](https://www.donet5.com/Doc/1/2436)、[GoldenDB](https://www.donet5.com/Doc/1/2642)、[Oscar](https://www.donet5.com/Doc/1/2369)、[GBase](https://www.donet5.com/Doc/1/2440)。
- 专用数据库：[MongoDB](https://www.donet5.com/Doc/1/2651)、[ClickHouse](https://www.donet5.com/Doc/1/2437)、[Doris](https://www.donet5.com/Doc/1/2577)、[TDengine](https://www.donet5.com/Doc/1/2566)、[DuckDB](https://www.donet5.com/Doc/1/2647)、[QuestDB](https://www.donet5.com/Doc/1/2434)、[DB2](https://www.donet5.com/Doc/1/2646)、[HANA](https://www.donet5.com/Doc/1/2645)。
