# HG、GBase、TDengine、DuckDB 驱动清单

后续 MongoDB 已新增六个[集合工具](mongodb-collections.md)，当前 catalog 为 62 个工具，显式固定 MongoDB.Driver 3.11.1。下列 56 tools 和回归数量保留为驱动补齐阶段的历史验收记录。

2026-11-20 补齐 NuGet 依赖，遵循 SqlSugar 官方扩展注册方式。版本按各扩展自身发布序列固定，不强制与 SqlSugarCore 版本相同。服务已统一注册 `InstanceFactory.CustomAssemblies`，使用者无需修改启动代码。

| 数据库 | 随包扩展 | 传递驱动依赖 | 目标主机要求 |
| --- | --- | --- | --- |
| HG | SqlSugar.HGCore 5.1.4.211 | SqlSugar.Nhgdb 5.1.4.159（Nhgdb.dll） | 正确的服务端、认证和 searchpath；使用专用 HG 驱动 |
| GBase 8s | SqlSugar.GBaseCore 5.1.4.277 | GeneralData.GBase8s.DataProvider 1.0.0.10 | 厂商 ClientSDK/ODBC、匹配平台/进程位数及 locale；事务库需 WITH LOG |
| TDengine | SqlSugar.TDengineCore 4.18.46 | TDengine.Ado.Data 3.1.7、TDengine.Connector 3.1.6 | TDengine 原生客户端 SDK；官方要求客户端版本不低于服务端，优先匹配版本 |
| DuckDB | SqlSugar.DuckDBCore 5.1.4.216 | DuckDB.NET.Data.Full / Bindings.Full 1.4.1 | 包含 win-x64、win-arm64、linux-x64、linux-arm64、osx 原生资产；无需数据库服务器 |

GBase 和 TDengine 的 NuGet 包只提供 .NET 层，不能代替厂商客户端安装。这里没有复制未经核实的 SDK 二进制，也没有把 Windows 客户端写死到跨平台包。原生依赖必须安装在运行 MCP 的主机或容器内，仅启动数据库服务器容器不会补齐主机依赖。

`available` 只表示扩展程序集可加载。离线预设、配置检查及能力查询不连接远程数据库。`get_database_capabilities` 的产品及能力验证状态继续按已有证据返回，不因补包就标记所有能力已验证。DB2/HANA 不在本次四个驱动的范围。

DuckDB 使用文件连接，路径相对于 MCP 工作目录解析。MCP 每次操作持有独占连接，因此 `:memory:` 不适合作为跨工具共享数据库。

## 发布框架与包大小

Full 包包含多个平台的原生库。实测双框架工具包约 299.3 MiB，超过 [NuGet.org 约 250 MB 上限](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package#package-size-limits)。因此发布工作流及安装验证仅打包 .NET 10，包大小约 149.1 MiB，保留全部平台原生资产；源码仍以 net9.0/net10.0 构建和测试。安装发布工具需要 .NET 10 SDK 及对应运行时（包含 ASP.NET Core shared framework）。

```powershell
dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c 'Release' '-p:TargetFrameworks=net10.0' '-p:BuildWebsite=true' --artifacts-path 'artifacts/package-build'
```

请使用此发布命令；省略 TargetFrameworks 会生成含重复原生资产的双框架大包。发布及包测试在超过 250,000,000 字节时失败，包测试同时断言仅有 net10 工具和完整 DuckDB 原生资产。

## 验证

- `Presets_LoadBundledProvidersWithoutOpeningConnections`：全部预设及程序集可用；HG/DuckDB 等验证连接构造及关闭状态；GBase/TDengine 验证驱动类型及离线能力诊断。GBase 连接构造会加载原生 SDK，TDengine 连接构造还会立即连接服务端，因此不能把它们当作纯离线构造测试。
- `BundledExtensions_RegisterTogetherWithoutPoisoningMongo`：四个扩展与 MongoDB 同进程注册。
- `BundledExtensionConfiguration_ReloadsAndReportsCapabilitiesOffline`：离线配置热更新及能力诊断，不影响原连接。
- `DuckDbTools_PersistTypedValuesAcrossLeasesAndRefreshSchema`：临时文件建表、类型参数、跨租约查询及元数据刷新。
- `scripts/test-package.ps1`：实际 pack → 临时 tool install → CLI/stdio 调用；再复制运行目录并缺省 HG 扩展，验证错误 1012 和其他连接隔离。不会修改正式安装或 NuGet 缓存文件。

2026-11-20 验证结果（Windows x64）：

- ORM 定向回归：net9.0 / net10.0 各 **47 passed，0 failed，0 skipped**。
- 非远程集成回归：net9.0 / net10.0 各 **404 passed，0 failed，0 skipped**，含 DuckDB 实际文件读写。
- MySQL 8.4、PostgreSQL 17、SQL Server 2022 Docker 集成：net9.0 / net10.0 各 **38 passed，0 failed，0 skipped**；已清理本轮隔离容器和匿名卷。
- NuGet.org 依赖审计：生产/测试项目及传递依赖均未报告已知漏洞。
- 临时发布包安装和真实 MCP stdio：**56 tools**，四类扩展能力查询、DuckDB 原生读写、类型参数均通过。副本缺少 HG DLL 时返回 **1012**，MongoDB、DuckDB 和 SQLite 仍可调用；无全局安装或远程发布。
- 包布局检查：仅包含 net10.0 工具，五个 DuckDB 原生资产目录完整；大小低于 250,000,000 字节。net10 使用 shared framework 中的 Hosting，net9 保留 Hosting 10.0.15 显式依赖。

HG、GBase、TDengine 没有对应实库及原生客户端环境，本轮未声称完成它们的认证、CRUD、DDL 或事务验收。其他操作系统仅核对包内原生资产，没有进行实机运行测试。

在仓库根目录复现：

```powershell
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --filter 'Category!=Integration'
pwsh -NoProfile -File 'scripts/test-integration.ps1'
pwsh -NoProfile -File 'scripts/test-package.ps1'
dotnet list 'DatabaseMcpServer.slnx' package --vulnerable --include-transitive
```

## 官方依据

- SqlSugar：[HG](https://www.donet5.com/Doc/1/2436)、[GBase](https://www.donet5.com/Doc/1/2440)、[TDengine](https://www.donet5.com/Doc/1/2566)、[DuckDB](https://www.donet5.com/Doc/1/2647)。
- NuGet：[HGCore](https://www.nuget.org/packages/SqlSugar.HGCore/5.1.4.211)、[GBaseCore](https://www.nuget.org/packages/SqlSugar.GBaseCore/5.1.4.277)、[TDengineCore](https://www.nuget.org/packages/SqlSugar.TDengineCore/4.18.46)、[DuckDBCore](https://www.nuget.org/packages/SqlSugar.DuckDBCore/5.1.4.216)。
