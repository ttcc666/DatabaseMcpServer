using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库配置服务 - DatabaseMcpServer 2.0.0+ 仅支持 JSON 配置文件模式。
/// </summary>
internal class DatabaseConfigService : IDatabaseConfigService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly Dictionary<string, DatabaseConnection> _connections = new();
    private readonly Dictionary<string, SqlSugarScope> _clientPool = new();
    private readonly object _poolLock = new();
    private string? _currentDatabaseName;

    public DatabaseConfigService(IConfiguration configuration, ILogger<DatabaseConfigService> logger, IDatabaseHelperService databaseHelper)
    {
        _configuration = configuration;
        _logger = logger;
        _databaseHelper = databaseHelper;
        LoadDatabaseConnections();
    }

    /// <summary>
    /// 加载数据库连接配置
    /// DatabaseMcpServer 2.0.0+ 仅支持 JSON 配置文件方式
    /// </summary>
    private void LoadDatabaseConnections()
    {
        // 首先检查是否使用了已废弃的环境变量
        CheckDeprecatedEnvironmentVariables();

        var configPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new InvalidOperationException(
                "未配置数据库连接。请设置 DB_CONFIG_PATH 环境变量指向 databases.json 配置文件。\n\n" +
                "示例:\n" +
                "  DB_CONFIG_PATH=D:\\config\\databases.json\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        _logger.LogInformation("使用配置文件路径: {Path}", configPath);

        if (!LoadFromConfigFile(configPath))
        {
            throw new InvalidOperationException(
                $"无法加载配置文件: {configPath}\n\n" +
                "请检查:\n" +
                "  1. 文件是否存在\n" +
                "  2. JSON 格式是否正确\n" +
                "  3. 文件编码是否为 UTF-8\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        _logger.LogInformation("已从配置文件加载 {Count} 个数据库连接", _connections.Count);
    }

    /// <summary>
    /// 从指定路径的配置文件加载多数据库配置
    /// </summary>
    private bool LoadFromConfigFile(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger.LogError("配置文件不存在: {Path}", configPath);
            return false;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<DatabasesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Databases == null || config.Databases.Count == 0)
            {
                _logger.LogError("配置文件中未找到有效的数据库配置: {Path}", configPath);
                return false;
            }

            foreach (var db in config.Databases)
            {
                _connections[db.Name] = db;

                // 设置默认数据库
                if (db.IsDefault || _currentDatabaseName == null)
                {
                    _currentDatabaseName = db.Name;
                }
            }

            _logger.LogInformation("成功从配置文件加载 {Count} 个数据库连接: {Path}", config.Databases.Count, configPath);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "配置文件 JSON 格式错误: {Path}", configPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法加载配置文件: {Path}", configPath);
            return false;
        }
    }

    /// <summary>
    /// 检测已废弃的环境变量配置（DatabaseMcpServer 2.0.0+）
    /// </summary>
    private void CheckDeprecatedEnvironmentVariables()
    {
        var deprecatedVars = new[]
        {
            "DB_CONNECTION_STRING",
            "DB_TYPE",
            "DB_DM_LOWERCASE_TABLES",
            "DB_KDBNDP_MODE",
            "DB_GAUSSDB_NATIVE_DRIVER",
            "DB_ORACLE_CAMEL_CASE",
            "DB_POSTGRES_AUTO_TO_LOWER",
            "DB_SQLITE_ENABLE_DEFAULT_VALUE",
            "DB_DISABLE_NVARCHAR",
            "DB_DDL_WHITELIST"
        };

        var foundVars = deprecatedVars
            .Where(v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)))
            .ToList();

        if (foundVars.Any())
        {
            var message = new System.Text.StringBuilder();
            message.AppendLine("检测到已废弃的环境变量配置（DatabaseMcpServer 2.0.0+）：");
            message.AppendLine();
            foreach (var v in foundVars)
            {
                message.AppendLine($"  - {v}");
            }
            message.AppendLine();
            message.AppendLine("从 2.0.0 版本开始，所有配置都必须在 databases.json 文件中设置。");
            message.AppendLine();
            message.AppendLine("迁移示例：");
            message.AppendLine("旧方式（环境变量）：");
            message.AppendLine("  DB_CONNECTION_STRING=Server=localhost;...");
            message.AppendLine("  DB_TYPE=MySql");
            message.AppendLine("  DB_DM_LOWERCASE_TABLES=true");
            message.AppendLine();
            message.AppendLine("新方式（databases.json）：");
            message.AppendLine("  {");
            message.AppendLine("    \"databases\": [{");
            message.AppendLine("      \"name\": \"default\",");
            message.AppendLine("      \"connectionString\": \"Server=localhost;...\",");
            message.AppendLine("      \"dbType\": \"MySql\",");
            message.AppendLine("      \"optimizationSettings\": {");
            message.AppendLine("        \"lowercaseTables\": \"true\"");
            message.AppendLine("      }");
            message.AppendLine("    }]");
            message.AppendLine("  }");
            message.AppendLine();
            message.AppendLine("详细迁移指南请参考 README.md 中的\"从 1.x 迁移到 2.0\"章节");

            throw new InvalidOperationException(message.ToString());
        }
    }

    /// <summary>
    /// 获取当前活动的数据库连接
    /// </summary>
    private DatabaseConnection GetCurrentConnection()
    {
        if (_connections.Count == 0)
        {
            throw new InvalidOperationException(
                "未配置任何数据库连接。请通过 databases.json 配置文件或环境变量 DB_CONNECTION_STRING 进行配置。");
        }

        if (_currentDatabaseName == null || !_connections.ContainsKey(_currentDatabaseName))
        {
            _currentDatabaseName = _connections.Keys.First();
        }

        return _connections[_currentDatabaseName];
    }

    /// <summary>
    /// 根据名称获取数据库连接
    /// </summary>
    private DatabaseConnection GetConnection(string databaseName)
    {
        if (!_connections.TryGetValue(databaseName, out var connection))
        {
            throw new InvalidOperationException($"数据库连接 '{databaseName}' 不存在。可用的连接: {string.Join(", ", _connections.Keys)}");
        }
        return connection;
    }

    /// <summary>
    /// 获取当前数据库连接字符串
    /// </summary>
    /// <returns>连接字符串</returns>
    public string GetConnectionString()
    {
        return GetCurrentConnection().ConnectionString;
    }

    /// <summary>
    /// 获取当前数据库类型
    /// </summary>
    /// <returns>数据库类型字符串</returns>
    public string GetDatabaseType()
    {
        return GetCurrentConnection().DbType;
    }

    /// <summary>
    /// 获取解析后的数据库类型
    /// </summary>
    /// <returns>对应的 DbType 枚举值</returns>
    public DbType GetParsedDbType()
    {
        var dbType = GetDatabaseType();
        return _databaseHelper.ParseDbType(dbType);
    }

    /// <summary>
    /// 创建数据库客户端（使用当前活动连接）
    /// 使用 SqlSugarScope 实现连接池复用，提高性能和线程安全性
    /// </summary>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    public ISqlSugarClient CreateClient()
    {
        var connection = GetCurrentConnection();
        return GetOrCreateScopedClient(connection);
    }

    /// <summary>
    /// 创建指定数据库的客户端
    /// 使用 SqlSugarScope 实现连接池复用，提高性能和线程安全性
    /// </summary>
    /// <param name="databaseName">数据库连接名称</param>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    public ISqlSugarClient CreateClient(string databaseName)
    {
        var connection = GetConnection(databaseName);
        return GetOrCreateScopedClient(connection);
    }

    /// <summary>
    /// 获取或创建 SqlSugarScope 客户端（线程安全的连接池实现）
    /// </summary>
    private ISqlSugarClient GetOrCreateScopedClient(DatabaseConnection connection)
    {
        // 检查连接池中是否已存在
        if (_clientPool.TryGetValue(connection.Name, out var existingScope))
        {
            _logger.LogDebug("复用连接池中的数据库客户端: {Name}", connection.Name);
            return existingScope;
        }

        // 使用锁确保线程安全
        lock (_poolLock)
        {
            // 双重检查
            if (_clientPool.TryGetValue(connection.Name, out existingScope))
            {
                return existingScope;
            }

            _logger.LogDebug("创建新的 SqlSugarScope 客户端: {Name}", connection.Name);

            var dbType = _databaseHelper.ParseDbType(connection.DbType);

            // 使用 SqlSugarScope 替代 SqlSugarClient，提供更好的线程安全性和连接池管理
            var scope = new SqlSugarScope(new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
                // 性能优化配置
                MoreSettings = CreateOptimizedSettings(dbType, connection)
            }, db =>
            {
                // 配置 SQL 执行日志
                db.Aop.OnLogExecuting = (sql, pars) =>
                {
                    var safeSql = SqlLogSanitizer.SanitizeSqlForLog(sql);
                    var safeParameters = SqlLogSanitizer.FormatParametersForLog(pars);
                    _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", safeSql, safeParameters);
                };

                // 配置错误日志
                db.Aop.OnError = (exp) =>
                {
                    var safeSql = SqlLogSanitizer.SanitizeSqlForLog(exp.Sql);
                    _logger.LogError("SQL执行错误: {Message} | SQL: {Sql}", exp.Message, safeSql);
                };
            });

            _clientPool[connection.Name] = scope;
            _logger.LogInformation("SqlSugarScope 客户端创建成功并加入连接池: {Name} ({DbType})", connection.Name, dbType);

            return scope;
        }
    }

    /// <summary>
    /// 根据数据库类型和连接配置创建优化的设置
    /// 使用策略模式，方便扩展新的数据库类型
    /// </summary>
    private ConnMoreSettings CreateOptimizedSettings(DbType dbType, DatabaseConnection connection)
    {
        var settings = new ConnMoreSettings
        {
            // 通用性能优化
            IsAutoRemoveDataCache = true, // 自动移除数据缓存，避免内存泄漏
            SqlServerCodeFirstNvarchar = true // SQL Server CodeFirst 使用 nvarchar
        };

        try
        {
            // 使用策略工厂获取对应数据库的优化策略
            var strategyFactory = new Strategies.DBSetting.DatabaseOptimizationStrategyFactory(_logger);
            var strategy = strategyFactory.GetStrategy(dbType);

            // 应用数据库特定的优化配置，传递 optimizationSettings
            strategy.ApplyOptimizations(settings, connection.OptimizationSettings);

            _logger.LogDebug("已为数据库 {Name} 应用性能优化: {Description}",
                connection.Name, strategy.GetDescription());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "应用数据库 {Name} 优化策略时出错，使用默认配置", connection.Name);
        }

        return settings;
    }

    /// <summary>
    /// 异步创建数据库客户端
    /// </summary>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    public async Task<ISqlSugarClient> CreateClientAsync()
    {
        return await Task.FromResult(CreateClient());
    }

    /// <summary>
    /// 验证数据库配置是否正确
    /// </summary>
    /// <returns>如果配置正确返回 true,否则返回 false</returns>
    public bool ValidateConfiguration()
    {
        try
        {
            GetConnectionString();
            GetParsedDbType();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取配置信息摘要
    /// </summary>
    /// <returns>配置信息 JSON 字符串</returns>
    public string GetConfigurationSummary()
    {
        var connection = GetCurrentConnection();
        var maskedConnectionString = MaskSensitiveInfo(connection.ConnectionString);

        return _databaseHelper.SerializeResult(new
        {
            configured = true,
            mode = _connections.Count > 1 ? "multi-database" : "single-database",
            totalDatabases = _connections.Count,
            currentDatabase = connection.Name,
            databaseType = connection.DbType,
            description = connection.Description,
            connectionString = maskedConnectionString,
            message = "配置有效"
        });
    }

    /// <summary>
    /// 获取所有可用的数据库连接列表
    /// </summary>
    /// <returns>数据库连接列表</returns>
    public List<DatabaseConnection> GetAllConnections()
    {
        return _connections.Values.ToList();
    }

    /// <summary>
    /// 切换当前活动的数据库连接
    /// </summary>
    /// <param name="databaseName">数据库连接名称</param>
    /// <returns>切换是否成功</returns>
    public bool SwitchDatabase(string databaseName)
    {
        if (!_connections.ContainsKey(databaseName))
        {
            _logger.LogWarning("尝试切换到不存在的数据库: {Name}", databaseName);
            return false;
        }

        _currentDatabaseName = databaseName;
        _logger.LogInformation("已切换到数据库: {Name}", databaseName);
        return true;
    }

    /// <summary>
    /// 获取当前活动的数据库连接名称
    /// </summary>
    /// <returns>当前数据库连接名称</returns>
    public string GetCurrentDatabaseName()
    {
        return GetCurrentConnection().Name;
    }

    /// <summary>
    /// 隐藏连接字符串中的敏感信息。
    /// </summary>
    /// <param name="connectionString">原始连接字符串</param>
    /// <returns>隐藏敏感信息后的连接字符串</returns>
    private static readonly System.Text.RegularExpressions.Regex SensitiveInfoPattern =
        new(@"(?i)(password|pwd)=([^;]*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 脱敏连接字符串中的敏感信息（如密码）。
    /// </summary>
    private static string MaskSensitiveInfo(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        return SensitiveInfoPattern.Replace(connectionString, match =>
        {
            var key = match.Groups[1].Value;
            return $"{key}=****";
        });
    }
}
