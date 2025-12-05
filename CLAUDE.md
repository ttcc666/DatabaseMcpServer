# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# DatabaseMcpServer 开发指南

DatabaseMcpServer 是一个基于 .NET 9.0 的 Model Context Protocol (MCP) 服务器，通过 stdio 传输协议为 AI 系统提供统一的数据库操作接口。

## 🏗️ 核心架构

### 分层架构设计
```
MCP Protocol Layer (stdio) → Tools Layer → Services Layer → Data Access Layer (SqlSugar ORM)
```

**关键组件关系**:
- `DatabaseConfigService`: 全局配置管理，通过 JSON 配置文件驱动
- `DatabaseHelper`: 核心工具类，提供数据库抽象和安全检查
- Tools 分层: Management(连接管理) / Query(查询) / Command(操作) / Schema(架构)
- 统一异常处理: `McpExceptionFilter` → `ApiResult<T>` → JSON 响应

### 依赖注入模式
所有服务通过 `Microsoft.Extensions.Hosting` 注册，工具类通过构造函数注入 `IDatabaseConfigService`。

## 🔧 开发命令

```bash
# 开发运行 (需要配置文件)
DB_CONFIG_PATH="path/to/databases.json" dotnet run

# 构建项目
dotnet build

# 发布版本 (自包含单文件)
dotnet publish -c Release -r win-x64 --self-contained

# 运行测试
dotnet test

# 打包 NuGet
dotnet pack -c Release
```

## 🛠️ 添加新 MCP 工具

1. **创建工具类** - 在 `Tools/` 相应目录下创建
   - `Tools/Management/` - 连接管理、Schema 管理
   - `Tools/Query/` - 查询操作
   - `Tools/Command/` - 数据操作（增删改）
2. **添加特性标注**:
   ```csharp
   [McpServerTool("tool_name")]
   public class YourTool
   {
       [Description("Tool description")]
       public async Task<ApiResult<T>> YourMethod(parameters) { }
   }
   ```
3. **注入数据库服务**:
   ```csharp
   private readonly IDatabaseConfigService _configService;
   // 使用 _configService.CreateClient() 获取数据库客户端
   ```
4. **注册工具** - 在 `Program.cs` 中添加: `.WithTools<YourTool>()`

## 🎯 添加新数据库优化策略

DatabaseMcpServer 使用 **策略模式 + 工厂模式** 实现数据库特定优化。

### 步骤 1: 创建策略类

在 `Strategies/` 目录下创建新策略类，实现 `IDatabaseOptimizationStrategy` 接口：

```csharp
public class YourDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public YourDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 应用数据库特定优化
        if (optimizationSettings == null) return;

        // 从 JSON 配置读取选项（使用小驼峰命名）
        if (optimizationSettings.TryGetValue("enableFeature", out var enableFeatureStr) &&
            bool.TryParse(enableFeatureStr, out var enableFeature))
        {
            // 应用配置
            _logger?.LogDebug("YourDB 特性启用: {Enabled}", enableFeature);
        }

        // 读取字符串配置
        if (optimizationSettings.TryGetValue("mode", out var mode))
        {
            _logger?.LogDebug("YourDB 模式: {Mode}", mode);
        }

        // 读取整数配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("YourDB 最大连接池大小: {MaxPoolSize}", maxPoolSize);
        }
    }

    public string GetDescription()
    {
        return "YourDB 性能优化：特性1 + 特性2 + 特性3";
    }
}
```

### 步骤 2: 注册到工厂

在 `DatabaseOptimizationStrategyFactory.cs` 的 `InitializeStrategyFactories()` 方法中添加映射：

```csharp
[DbType.YourDb] = () => new YourDbOptimizationStrategy(_logger),
```

### 步骤 3: 创建配置文档

在 `DatabaseSetting/` 目录下创建配置文档（参考现有文档格式）：
- 连接字符串参数详解
- JSON 优化配置说明
- 完整配置示例
- 常见问题及解决方案
- 性能优化建议

### JSON 配置键名规范

使用小驼峰命名（camelCase）：

```json
{
  "optimizationSettings": {
    "enableFeature": "true",
    "lowercaseTables": "true",
    "maxPoolSize": "100",
    "mode": "oracle"
  }
}
```

**常用配置键名示例**:
- `lowercaseTables` (bool) - 使用小写表名
- `enableFeature` (bool) - 启用特定特性
- `maxPoolSize` (int) - 最大连接池大小
- `mode` (string) - 兼容模式

详细指南: `Doc/extending-database-optimization.md`

## 🔒 安全机制

### 危险操作检测
`DatabaseHelper.IsDangerousOperation()` 自动检测以下操作:
- DROP (表/数据库删除)
- TRUNCATE (表清空)
- ALTER (结构修改)
- DELETE (无 WHERE 条件)
- UPDATE (无 WHERE 条件)

### 参数化查询
所有 SQL 执行都通过 SqlSugar 的参数化机制，防止 SQL 注入。

### 敏感信息保护
连接字符串中的密码自动替换为 `***` 进行日志记录。

## 🌐 环境配置

### 必需环境变量
- `DB_CONFIG_PATH`: 数据库配置文件路径（必需，指向 databases.json 文件）

### 可选环境变量（日志记录）
- `SEQ_SERVER_URL`: Seq 日志服务器地址 (如 http://localhost:5341)
- `SEQ_API_KEY`: Seq API 密钥（用于认证和高级功能）

### 可选环境变量（安全配置）
- `DB_DDL_WHITELIST`: DDL 操作白名单（逗号分隔的正则表达式列表）

### 数据库特定优化配置（2.0.1-test+）

从 2.0.1-test 版本开始，所有数据库特定优化配置都在 `databases.json` 的 `optimizationSettings` 中设置。
不再使用环境变量（如 `DB_DM_LOWERCASE_TABLES`）。

**配置示例**:

```json
{
  "databases": [
    {
      "name": "dm_production",
      "connectionString": "Server=localhost;Database=test;User=SYSDBA;Password=SYSDBA;",
      "dbType": "dm",
      "isDefault": true,
      "optimizationSettings": {
        "lowercaseTables": "true",
        "dockerMysqlMode": "true",
        "clobOptimization": "true"
      }
    },
    {
      "name": "kdbndp_production",
      "connectionString": "Server=localhost;Port=54321;Database=test;User=system;Password=password;",
      "dbType": "kdbndp",
      "optimizationSettings": {
        "mode": "oracle",
        "enableCursor": "true",
        "enableJson": "true"
      }
    }
  ]
}
```

详细配置说明请参考 `DatabaseSetting/` 目录下各数据库的配置文档。

### MCP 配置示例

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer.exe",
      "env": {
        "DB_CONFIG_PATH": "D:/config/databases.json",
        "SEQ_SERVER_URL": "http://localhost:5341",
        "SEQ_API_KEY": "your-seq-api-key",
        "DB_DDL_WHITELIST": "^CREATE TABLE.*,^ALTER TABLE.*ADD COLUMN.*"
      }
    }
  }
}
```

## 📊 数据库支持

**支持的数据库类型** (共34种):

### 🌐 主流数据库
- MySQL (默认)
- SQL Server
- SQLite
- PostgreSQL
- Oracle

### 🇨🇳 国产数据库
- 达梦数据库 (dm)
- 人大金仓 (kdbndp/kingbase)
- 神通数据库 (oscar)
- 瀚高数据库 (hg)
- 南大通用 GBase (gbase)
- 虚谷数据库 (xugu)
- 海量数据库 (vastbase)
- GoldenDB (goldendb)

### 🚀 分布式数据库
- OceanBase (oceanbase)
- TiDB (tidb)
- PolarDB (polardb)
- Doris (doris)

### ⏱️ 时序数据库
- TDengine (tdengine)
- QuestDB (questdb)
- ClickHouse (clickhouse)

### 🔍 分析型数据库
- DuckDB (duckdb)

### 🛠️ 其他数据库
- Microsoft Access (access)
- ODBC (odbc)
- SAP HANA (hana)
- IBM DB2 (db2)
- MongoDB (mongodb)
- 自定义数据库 (custom)

### 🔧 特定版本和变体
- MySQL Connector (mysqlconnector)
- OpenGauss (opengauss)
- GaussDB (gaussdb)
- GaussDB Native (gaussdbnative)
- OceanBase for Oracle (oceanbasefororacle)
- TDSQL (tdsql)
- TDSQL for PG ODBC (tdsqlforpgodbc)

**ORM 框架**: SqlSugarCore 5.1.4 - 轻量级 ORM，支持多数据库和复杂查询。

## 🔄 错误处理模式

### 统一异常处理流程
```
Exception → DatabaseMcpException → McpExceptionFilter → ApiResult<T> → JSON Response
```

**自定义异常类型**:
- `DatabaseMcpException`: 业务异常
- `DatabaseErrorCode`: 标准化错误码枚举

### 返回结果包装
所有 API 返回都使用 `ApiResult<T>` 包装:
```csharp
return ApiResult<T>.Success(data);
return ApiResult<T>.Error("错误信息", DatabaseErrorCode.ConnectionFailed);
```

## 🚀 部署特性

- **自包含应用**: 无需安装 .NET 运行时
- **单文件可执行**: 简化部署和分发
- **跨平台支持**: Windows/macOS/Linux (x64/ARM64)
- **stdio 传输**: 通过标准输入输出与 AI 系统通信

## 🔍 调试和测试

### 连接测试
使用 `ConnectionTools.TestConnection()` 验证数据库连接。

### 配置验证
使用 `ConnectionTools.ValidateConfiguration()` 检查配置完整性。

### 日志记录
所有数据库操作和异常都会记录到控制台，便于调试。

## 📚 文档结构

### 核心文档
- `README.md` - 项目概述和快速开始
- `CLAUDE.md` - 开发指南（本文件）
- `MULTI_DATABASE_GUIDE.md` - 多数据库配置指南

### 数据库配置文档 (`DatabaseSetting/`)
每个数据库都有详细的配置文档，包含：
- 连接字符串参数详解
- JSON 优化配置说明
- 完整配置示例（生产/开发/特殊场景）
- 常见问题及解决方案
- 性能优化建议

**主流数据库**: MySQL.md, SQLServer.md, Oracle.md, PostgreSQL.md, SQLite.md
**国产数据库**: DM.md, Kdbndp.md, GaussDB.md
**时序数据库**: QuestDB.md

### 技术文档 (`Doc/`)
- `extending-database-optimization.md` - 扩展数据库优化策略指南
- `donet5_*.md` - 各数据库的 .NET 操作指南

## 🔑 关键设计模式

### 1. 策略模式 (Database Optimization)
- **接口**: `IDatabaseOptimizationStrategy`
- **工厂**: `DatabaseOptimizationStrategyFactory`
- **实现**: 每个数据库一个策略类（如 `MySqlOptimizationStrategy`）
- **优势**: 易于扩展新数据库，低耦合，独立测试

### 2. 依赖注入 (Services)
- **配置服务**: `IDatabaseConfigService` → `DatabaseConfigService`
- **辅助服务**: `IDatabaseHelperService` → `DatabaseHelper`
- **注册**: `Program.cs` 中通过 `AddSingleton` 注册

### 3. 连接池管理 (SqlSugarScope)
- 使用 `SqlSugarScope` 实现连接池复用
- 每个数据库连接维护独立的 `SqlSugarScope` 实例
- 线程安全的连接池访问（`_poolLock`）

### 4. 统一异常处理
- **过滤器**: `McpExceptionFilter` 拦截所有异常
- **包装**: `ApiResult<T>` 统一返回格式
- **错误码**: `DatabaseErrorCode` 枚举标准化错误

## 🛡️ 安全最佳实践

### SQL 注入防护
- ✅ 所有查询使用 SqlSugar 参数化
- ✅ 危险操作自动检测（DROP/TRUNCATE/ALTER）
- ❌ 禁止直接拼接 SQL 字符串

### 敏感信息保护
- ✅ 连接字符串密码自动脱敏（日志中显示为 `***`）
- ✅ JSON 配置文件驱动（不在代码中硬编码）
- ❌ 禁止在日志中输出完整连接字符串

### 危险操作白名单
- 某些 DDL 操作（如 CodeFirst 迁移）需要白名单支持
- 白名单配置: `DatabaseHelper.LoadWhitelistPatterns()`