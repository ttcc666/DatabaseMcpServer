# DatabaseMcpServer 安全重构执行计划

## 目标
- 在不影响当前 MCP tool 名称、参数与返回结构的前提下重构内部结构
- 将宿主启动和 MCP 注册改为更显式的官方 SDK 风格
- 补最小回归护栏，降低后续维护成本

## 不变量
- 保持 `stdio` transport
- 保持 `databases.json` 配置模式
- 保持现有多数据库切换能力
- 保持现有工具契约和主要返回字段

## 实施步骤
1. 抽出宿主启动基础设施
   - 新增服务注册扩展
   - 新增 SqlSugar provider warmup 组件
   - 将 `Program.cs` 收敛为启动编排
2. 拆分连接创建职责
   - 新增 `ISqlSugarClientFactory`
   - 将 SqlSugarScope 缓存、AOP、优化策略应用从 `DatabaseConfigService` 中移出
3. 收敛工具层执行骨架
   - 新增 `McpToolBase`
   - 统一异常包装、成功结果序列化、客户端创建模式
4. 去重 JSON/参数解析
   - 抽出 `JsonElement` 值转换
   - 抽出 Schema 列定义 JSON 解析器
5. 补最小测试
   - 参数解析
   - SQL 安全守卫
   - 异常 JSON 包装
   - DI/宿主注册 smoke test

## 验证
- `dotnet build 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -f 'net9.0'`
- `dotnet test 'tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'`
- 如环境允许，再补 `dotnet build 'DatabaseMcpServer.slnx'`
