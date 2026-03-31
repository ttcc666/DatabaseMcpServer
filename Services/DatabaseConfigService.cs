using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库配置服务，负责加载连接配置并维护当前活动连接。
/// </summary>
internal class DatabaseConfigService : IDatabaseConfigService
{
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ISqlSugarClientFactory _clientFactory;
    private readonly IJsonResultSerializer _resultSerializer;
    private readonly Dictionary<string, DatabaseConnection> _connections = new(StringComparer.Ordinal);
    private string? _currentDatabaseName;

    public DatabaseConfigService(
        ILogger<DatabaseConfigService> logger,
        IDatabaseHelperService databaseHelper,
        ISqlSugarClientFactory clientFactory,
        IJsonResultSerializer resultSerializer)
    {
        _logger = logger;
        _databaseHelper = databaseHelper;
        _clientFactory = clientFactory;
        _resultSerializer = resultSerializer;

        LoadDatabaseConnections();
    }

    private void LoadDatabaseConnections()
    {
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

            _connections.Clear();
            _currentDatabaseName = null;

            foreach (var db in config.Databases)
            {
                _connections[db.Name] = db;
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

        if (!foundVars.Any())
        {
            return;
        }

        var message = new System.Text.StringBuilder();
        message.AppendLine("检测到已废弃的环境变量配置（DatabaseMcpServer 2.0.0+）：");
        message.AppendLine();

        foreach (var variable in foundVars)
        {
            message.AppendLine($"  - {variable}");
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

    private DatabaseConnection GetCurrentConnection()
    {
        if (_connections.Count == 0)
        {
            throw new InvalidOperationException(
                "未配置任何数据库连接。请设置 DB_CONFIG_PATH 环境变量指向 databases.json 配置文件。");
        }

        if (_currentDatabaseName == null || !_connections.ContainsKey(_currentDatabaseName))
        {
            _currentDatabaseName = _connections.Keys.First();
        }

        return _connections[_currentDatabaseName];
    }

    private DatabaseConnection GetConnection(string databaseName)
    {
        if (!_connections.TryGetValue(databaseName, out var connection))
        {
            throw new InvalidOperationException($"数据库连接 '{databaseName}' 不存在。可用的连接: {string.Join(", ", _connections.Keys)}");
        }

        return connection;
    }

    public string GetConnectionString()
    {
        return GetCurrentConnection().ConnectionString;
    }

    public string GetDatabaseType()
    {
        return GetCurrentConnection().DbType;
    }

    public DbType GetParsedDbType()
    {
        return _databaseHelper.ParseDbType(GetDatabaseType());
    }

    public ISqlSugarClient CreateClient()
    {
        return _clientFactory.CreateClient(GetCurrentConnection());
    }

    public ISqlSugarClient CreateClient(string databaseName)
    {
        return _clientFactory.CreateClient(GetConnection(databaseName));
    }

    public Task<ISqlSugarClient> CreateClientAsync()
    {
        return Task.FromResult(CreateClient());
    }

    public bool ValidateConfiguration()
    {
        try
        {
            _ = GetConnectionString();
            _ = GetParsedDbType();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetConfigurationSummary()
    {
        var connection = GetCurrentConnection();

        return _resultSerializer.Serialize(new
        {
            configured = true,
            mode = _connections.Count > 1 ? "multi-database" : "single-database",
            totalDatabases = _connections.Count,
            currentDatabase = connection.Name,
            databaseType = connection.DbType,
            description = connection.Description,
            connectionString = MaskSensitiveInfo(connection.ConnectionString),
            message = "配置有效"
        });
    }

    public List<DatabaseConnection> GetAllConnections()
    {
        return _connections.Values.ToList();
    }

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

    public string GetCurrentDatabaseName()
    {
        return GetCurrentConnection().Name;
    }

    private static readonly System.Text.RegularExpressions.Regex SensitiveInfoPattern =
        new(@"(?i)(password|pwd)=([^;]*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string MaskSensitiveInfo(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        return SensitiveInfoPattern.Replace(connectionString, match =>
        {
            var key = match.Groups[1].Value;
            return $"{key}=****";
        });
    }
}
