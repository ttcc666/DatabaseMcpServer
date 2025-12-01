# 扩展数据库优化策略指南

本文档说明如何为 DatabaseMcpServer 添加新的数据库类型优化策略。

---

## 🏗️ 架构设计

DatabaseMcpServer 使用 **策略模式 + 工厂模式** 实现数据库优化配置，具有以下优势：

- ✅ **高扩展性**: 添加新数据库类型无需修改现有代码
- ✅ **低耦合**: 每个数据库的优化逻辑独立封装
- ✅ **易维护**: 策略类职责单一，易于理解和修改
- ✅ **可测试**: 每个策略可独立测试

### 核心组件

```
IDatabaseOptimizationStrategy (接口)
    ↓
具体策略实现 (MySqlOptimizationStrategy, SqlServerOptimizationStrategy, etc.)
    ↓
DatabaseOptimizationStrategyFactory (工厂)
    ↓
DatabaseHelper / DatabaseConfigService (使用方)
```

---

## 📝 添加新数据库优化策略

### 步骤 1: 创建策略类

在 `Strategies/` 目录下创建新的策略类，实现 `IDatabaseOptimizationStrategy` 接口。

**示例**: 为 PostgreSQL 添加优化策略

```csharp
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// PostgreSQL 性能优化策略
/// </summary>
public class PostgreSqlOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public PostgreSqlOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // PostgreSQL 特定优化配置
        if (optimizationSettings == null) return;

        // 示例 1: 从 JSON 配置读取 JIT 编译选项
        if (optimizationSettings.TryGetValue("enableJit", out var enableJitStr) &&
            bool.TryParse(enableJitStr, out var enableJit))
        {
            // 应用 JIT 编译优化
            _logger?.LogDebug("PostgreSQL 启用 JIT 编译: {Enabled}", enableJit);
        }

        // 示例 2: 从 JSON 配置读取连接池参数
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            // 应用连接池配置
            _logger?.LogDebug("PostgreSQL 最大连接池大小: {MaxPoolSize}", maxPoolSize);
        }

        _logger?.LogDebug("应用 PostgreSQL 性能优化配置");
    }

    public string GetDescription()
    {
        return "PostgreSQL 性能优化：连接池 + JIT 编译 + 查询优化";
    }
}
```

### 步骤 2: 注册到工厂

在 `DatabaseOptimizationStrategyFactory.cs` 的 `InitializeStrategyFactories()` 方法中添加映射：

```csharp
private Dictionary<DbType, Func<IDatabaseOptimizationStrategy>> InitializeStrategyFactories()
{
    return new Dictionary<DbType, Func<IDatabaseOptimizationStrategy>>
    {
        // ... 现有映射 ...

        // 添加新的 PostgreSQL 策略
        [DbType.PostgreSQL] = () => new PostgreSqlOptimizationStrategy(_logger),

        // ... 其他映射 ...
    };
}
```

### 步骤 3: 测试

编译并测试新策略：

```bash
dotnet build
```

---

## 🎯 实际案例

### 案例 1: MySQL 优化策略

**文件**: `Strategies/MySqlOptimizationStrategy.cs`

```csharp
public class MySqlOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public MySqlOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // MySQL 不需要禁用 nvarchar
        settings.DisableNvarchar = false;

        // 从 JSON 配置读取优化选项（如果有）
        if (optimizationSettings != null)
        {
            // 示例：读取字符集配置
            if (optimizationSettings.TryGetValue("charset", out var charset))
            {
                _logger?.LogDebug("MySQL 字符集: {Charset}", charset);
            }
        }

        _logger?.LogDebug("应用 MySQL 性能优化配置");
    }

    public string GetDescription()
    {
        return "MySQL 性能优化：utf8mb4 字符集支持 + 连接池配置";
    }
}
```

**注册**:
```csharp
[DbType.MySql] = () => new MySqlOptimizationStrategy(_logger),
[DbType.MySqlConnector] = () => new MySqlOptimizationStrategy(_logger),
[DbType.Tidb] = () => new MySqlOptimizationStrategy(_logger), // TiDB 兼容 MySQL
```

### 案例 2: SQL Server 优化策略

**文件**: `Strategies/SqlServerOptimizationStrategy.cs`

```csharp
public class SqlServerOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public SqlServerOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 启用 NoLock 提高并发读取性能
        settings.IsWithNoLockQuery = true;

        // 事务中禁用 NoLock
        settings.DisableWithNoLockWithTran = true;

        // 默认不禁用 nvarchar，可通过 JSON 配置覆盖
        settings.DisableNvarchar = false;

        // 从 JSON 配置读取是否禁用 nvarchar（性能优化）
        if (optimizationSettings != null &&
            optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            _logger?.LogDebug("SQL Server 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        _logger?.LogDebug("应用 SQL Server 性能优化配置（NoLock: {NoLock}）", settings.IsWithNoLockQuery);
    }

    public string GetDescription()
    {
        return "SQL Server 性能优化：自动 NoLock + 连接池 + 可选禁用 nvarchar";
    }
}
```

### 案例 3: Oracle 优化策略

**文件**: `Strategies/OracleOptimizationStrategy.cs`

```csharp
public class OracleOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public OracleOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认转大写，可通过 JSON 配置覆盖
        settings.IsAutoToUpper = true;

        if (optimizationSettings == null) return;

        // 从 JSON 配置读取是否使用驼峰表名
        if (optimizationSettings.TryGetValue("camelCase", out var camelCaseStr) &&
            bool.TryParse(camelCaseStr, out var camelCase))
        {
            settings.IsAutoToUpper = !camelCase;
            _logger?.LogDebug("Oracle 使用驼峰表名: {CamelCase}", camelCase);
        }

        // 从 JSON 配置读取 Oracle 12C+ 原生自增支持
        if (optimizationSettings.TryGetValue("enableIdentity", out var enableIdentityStr) &&
            bool.TryParse(enableIdentityStr, out var enableIdentity))
        {
            settings.EnableOracleIdentity = enableIdentity;
            _logger?.LogDebug("Oracle 启用原生自增: {Enabled}", enableIdentity);
        }

        // 从 JSON 配置读取 Oracle 11 参数名长度限制
        if (optimizationSettings.TryGetValue("maxParamLength", out var maxParamLengthStr) &&
            int.TryParse(maxParamLengthStr, out var maxParamLength))
        {
            settings.MaxParameterNameLength = maxParamLength;
            _logger?.LogDebug("Oracle 参数名最大长度: {MaxLength}", maxParamLength);
        }

        _logger?.LogDebug("应用 Oracle 性能优化配置");
    }

    public string GetDescription()
    {
        return "Oracle 性能优化：大连接池 + 智能表名处理 + 原生自增支持";
    }
}
```

---

## 🔧 JSON 配置选项

策略类通过 `optimizationSettings` 字典读取用户自定义配置：

```csharp
public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
{
    if (optimizationSettings == null) return;

    // 读取布尔值
    if (optimizationSettings.TryGetValue("enableFeature", out var enableFeatureStr) &&
        bool.TryParse(enableFeatureStr, out var enableFeature))
    {
        // 应用配置
        _logger?.LogDebug("启用特性: {Enabled}", enableFeature);
    }

    // 读取整数值
    if (optimizationSettings.TryGetValue("maxValue", out var maxValueStr) &&
        int.TryParse(maxValueStr, out var maxValue))
    {
        settings.SomeSetting = maxValue;
        _logger?.LogDebug("最大值: {MaxValue}", maxValue);
    }

    // 读取字符串值
    if (optimizationSettings.TryGetValue("customValue", out var customValue))
    {
        // 应用配置
        _logger?.LogDebug("自定义值: {CustomValue}", customValue);
    }
}
```

**JSON 配置示例**:
```json
{
  "databases": [
    {
      "name": "my-database",
      "connectionString": "...",
      "dbType": "MySql",
      "optimizationSettings": {
        "enableFeature": "true",
        "maxValue": "100",
        "customValue": "custom-setting"
      }
    }
  ]
}
```

---

## 📊 已支持的数据库类型

当前 DatabaseMcpServer 已为以下数据库类型配置了优化策略：

### 🎯 专用优化策略

| 数据库 | 策略类 | 优化特性 |
|--------|--------|---------|
| MySQL / MariaDB | `MySqlOptimizationStrategy` | utf8mb4 + 连接池 |
| SQL Server | `SqlServerOptimizationStrategy` | NoLock + 可选禁用 nvarchar |
| Oracle | `OracleOptimizationStrategy` | 表名处理 + 原生自增 |
| PostgreSQL | `PostgreSqlOptimizationStrategy` | 连接池 + JIT 编译 + 查询优化 |
| SQLite | `SqliteOptimizationStrategy` | 轻量级优化 + 文件数据库特性 |
| 达梦数据库 | `DmOptimizationStrategy` | 表名处理 + Docker 模式兼容 + Clob 优化 |
| 人大金仓 | `KdbndpOptimizationStrategy` | 多模式兼容 + 游标支持 + JSON/Geometry |
| GaussDB/OpenGauss | `GaussDbOptimizationStrategy` | 原生驱动 + Schema 管理 + 类型映射 |
| QuestDB | `QuestDbOptimizationStrategy` | WAL 异步写入 + Symbol 优化 + 时间分区 |

### 🔄 兼容策略

| 数据库 | 使用策略 | 说明 |
|--------|---------|------|
| TiDB | `MySqlOptimizationStrategy` | 兼容 MySQL 协议 |
| PolarDB | `MySqlOptimizationStrategy` | 兼容 MySQL 协议 |
| Doris | `MySqlOptimizationStrategy` | 兼容 MySQL 协议 |
| TDSQL | `MySqlOptimizationStrategy` | 兼容 MySQL 协议 |
| OceanBase for Oracle | `OracleOptimizationStrategy` | 兼容 Oracle 协议 |

### 📦 默认策略

其他数据库类型使用 `DefaultOptimizationStrategy`，应用通用优化配置。

---

## 🚀 最佳实践

### 1. 策略类命名规范

```
{DatabaseName}OptimizationStrategy.cs
```

示例:
- `MySqlOptimizationStrategy.cs`
- `SqlServerOptimizationStrategy.cs`
- `PostgreSqlOptimizationStrategy.cs`

### 2. 日志记录

在策略类中使用日志记录优化配置：

```csharp
_logger?.LogDebug("应用 {DbType} 性能优化配置", "MySQL");
_logger?.LogDebug("启用特性: {Feature} = {Value}", "NoLock", true);
```

### 3. JSON 配置键名规范

使用 **camelCase** 命名规范：

```
{feature}
```

示例:
- `disableNvarchar` (SQL Server)
- `camelCase` (Oracle)
- `enableJit` (PostgreSQL)
- `maxPoolSize` (通用)
- `lowercaseTables` (达梦数据库)
- `nativeDriver` (GaussDB)

### 4. 描述信息

`GetDescription()` 方法应返回简洁的优化说明：

```csharp
public string GetDescription()
{
    return "数据库名称 性能优化：特性1 + 特性2 + 特性3";
}
```

### 5. 向后兼容

添加新配置时，应保持向后兼容：

```csharp
public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
{
    // ✅ 推荐：默认值 + 可选覆盖
    settings.SomeFeature = true; // 默认启用

    if (optimizationSettings != null &&
        optimizationSettings.TryGetValue("someFeature", out var featureStr) &&
        bool.TryParse(featureStr, out var feature))
    {
        settings.SomeFeature = feature; // 用户可选覆盖
    }

    // ❌ 避免：强制要求配置
    if (optimizationSettings == null || !optimizationSettings.ContainsKey("required"))
    {
        throw new Exception("必须配置 required 选项"); // 破坏向后兼容
    }
}
```

---

## 🧪 测试策略

### 单元测试示例

```csharp
[Fact]
public void MySqlOptimizationStrategy_ShouldApplyCorrectSettings()
{
    // Arrange
    var strategy = new MySqlOptimizationStrategy();
    var settings = new ConnMoreSettings();

    // Act
    strategy.ApplyOptimizations(settings, null);

    // Assert
    Assert.False(settings.DisableNvarchar);
}

[Fact]
public void SqlServerOptimizationStrategy_ShouldEnableNoLock()
{
    // Arrange
    var strategy = new SqlServerOptimizationStrategy();
    var settings = new ConnMoreSettings();

    // Act
    strategy.ApplyOptimizations(settings, null);

    // Assert
    Assert.True(settings.IsWithNoLockQuery);
    Assert.True(settings.DisableWithNoLockWithTran);
}

[Fact]
public void SqlServerOptimizationStrategy_ShouldRespectDisableNvarcharSetting()
{
    // Arrange
    var strategy = new SqlServerOptimizationStrategy();
    var settings = new ConnMoreSettings();
    var optimizationSettings = new Dictionary<string, string>
    {
        ["disableNvarchar"] = "true"
    };

    // Act
    strategy.ApplyOptimizations(settings, optimizationSettings);

    // Assert
    Assert.True(settings.DisableNvarchar);
}
```

---

## 📚 相关文档

- [主 README](../README.md)

---

## 💡 贡献指南

欢迎为 DatabaseMcpServer 贡献新的数据库优化策略！

### 贡献步骤

1. Fork 项目仓库
2. 创建策略类 (`Strategies/{DatabaseName}OptimizationStrategy.cs`)
3. 在工厂类中注册策略
4. 添加单元测试
5. 更新文档
6. 提交 Pull Request

### 代码审查要点

- ✅ 策略类实现 `IDatabaseOptimizationStrategy` 接口
- ✅ 包含详细的日志记录
- ✅ 环境变量命名规范
- ✅ 向后兼容
- ✅ 包含单元测试
- ✅ 更新相关文档

---

**最后更新**: 2025-12-01 (DatabaseMcpServer 2.0.0)
