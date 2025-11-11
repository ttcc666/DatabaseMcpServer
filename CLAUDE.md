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
- `DatabaseConfigService`: 全局配置管理，通过环境变量驱动
- `DatabaseHelper`: 核心工具类，提供数据库抽象和安全检查
- Tools 分层: Management(连接管理) / Query(查询) / Command(操作) / Schema(架构)
- 统一异常处理: `McpExceptionFilter` → `ApiResult<T>` → JSON 响应

### 依赖注入模式
所有服务通过 `Microsoft.Extensions.Hosting` 注册，工具类通过构造函数注入 `IDatabaseConfigService`。

## 🔧 开发命令

```bash
# 开发运行 (需要环境变量)
DB_CONNECTION_STRING="your_connection" DB_TYPE="MySql" dotnet run

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
- `DB_CONNECTION_STRING`: 数据库连接字符串
- `DB_TYPE`: 数据库类型 (主流数据库: MySql/SqlServer/Sqlite/PostgreSQL/Oracle, 国产数据库: dm/kdbndp/kingbase/oscar/hg/gbase/xugu/vastbase/goldendb, 分布式数据库: oceanbase/tidb/polardb/doris, 时序数据库: tdengine/questdb/clickhouse, 其他数据库: duckdb/access/odbc/hana/db2/mongodb/custom等)

### 可选环境变量（日志记录）
- `SEQ_SERVER_URL`: Seq 日志服务器地址 (如 http://localhost:5341)
- `SEQ_API_KEY`: Seq API 密钥（用于认证和高级功能）

### MCP 配置示例
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer.exe",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;Uid=root;Pwd=password;",
        "DB_TYPE": "MySql",
        "SEQ_SERVER_URL": "http://localhost:5341",
        "SEQ_API_KEY": "your-seq-api-key"
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