using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库配置服务 - 支持单数据库（环境变量）和多数据库（配置文件）模式。
/// </summary>
internal class DatabaseConfigService : IDatabaseConfigService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly Dictionary<string, DatabaseConnection> _connections = new();
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
    /// 优先级：
    /// 1. DB_CONFIG_PATH 环境变量指定的配置文件
    /// 2. DB_CONNECTION_STRING 和 DB_TYPE 环境变量（单数据库模式）
    /// 如果两种都未配置，抛出异常
    /// </summary>
    private void LoadDatabaseConnections()
    {
        var configPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

        // 情况 1: 指定了配置文件路径
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            _logger.LogInformation("使用配置文件路径: {Path}", configPath);

            if (LoadFromConfigFile(configPath))
            {
                _logger.LogInformation("已从配置文件加载 {Count} 个数据库连接", _connections.Count);
                return;
            }

            // 配置文件加载失败，记录错误
            _logger.LogError("配置文件加载失败: {Path}", configPath);
        }

        // 情况 2: 使用环境变量（单数据库模式）
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogInformation("使用环境变量配置（单数据库模式）");
            LoadFromEnvironmentVariables();
            return;
        }

        // 情况 3: 两种都未配置，抛出异常
        throw new InvalidOperationException(
            "未配置数据库连接。请配置以下任一方式：\n" +
            "1. 设置环境变量 DB_CONFIG_PATH 指向配置文件路径\n" +
            "2. 设置环境变量 DB_CONNECTION_STRING 和 DB_TYPE（单数据库模式）");
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
    /// 从环境变量加载单数据库配置（兼容模式）
    /// </summary>
    private void LoadFromEnvironmentVariables()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        var dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MySql";

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var connection = new DatabaseConnection
            {
                Name = "default",
                ConnectionString = connectionString,
                DbType = dbType,
                Description = "从环境变量加载的默认数据库",
                IsDefault = true
            };

            _connections["default"] = connection;
            _currentDatabaseName = "default";

            _logger.LogInformation("已从环境变量加载默认数据库连接");
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
    /// 从环境变量读取数据库连接字符串（已弃用，保留向后兼容）
    /// </summary>
    /// <returns>连接字符串,如果未配置则抛出异常</returns>
    /// <exception cref="InvalidOperationException">当环境变量未配置时抛出</exception>
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
    /// </summary>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public SqlSugarClient CreateClient()
    {
        var connection = GetCurrentConnection();
        _logger.LogDebug("正在创建数据库客户端连接: {Name}", connection.Name);

        var dbType = _databaseHelper.ParseDbType(connection.DbType);
        var client = _databaseHelper.CreateClient(connection.ConnectionString, dbType);

        _logger.LogInformation("数据库客户端连接创建成功: {Name} ({DbType})", connection.Name, dbType);
        return client;
    }

    /// <summary>
    /// 创建指定数据库的客户端
    /// </summary>
    /// <param name="databaseName">数据库连接名称</param>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public SqlSugarClient CreateClient(string databaseName)
    {
        var connection = GetConnection(databaseName);
        _logger.LogDebug("正在创建指定数据库客户端连接: {Name}", connection.Name);

        var dbType = _databaseHelper.ParseDbType(connection.DbType);
        var client = _databaseHelper.CreateClient(connection.ConnectionString, dbType);

        _logger.LogInformation("指定数据库客户端连接创建成功: {Name} ({DbType})", connection.Name, dbType);
        return client;
    }

    /// <summary>
    /// 异步创建数据库客户端
    /// </summary>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public async Task<SqlSugarClient> CreateClientAsync()
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