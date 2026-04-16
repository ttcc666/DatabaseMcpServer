using DatabaseMcpServer.Helpers;
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
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, DatabaseConnection> _connections = new(StringComparer.Ordinal);
    private string _configPath = string.Empty;
    private string? _currentDatabaseName;

    public DatabaseConfigService(
        ILogger<DatabaseConfigService> logger,
        IDatabaseHelperService databaseHelper,
        ISqlSugarClientFactory clientFactory,
        IJsonResultSerializer resultSerializer,
        ICurrentDatabaseStateStore currentDatabaseStateStore)
    {
        _logger = logger;
        _databaseHelper = databaseHelper;
        _clientFactory = clientFactory;
        _resultSerializer = resultSerializer;
        _currentDatabaseStateStore = currentDatabaseStateStore;

        LoadDatabaseConnections();
    }

    private void LoadDatabaseConnections()
    {
        var configPath = GetConfiguredPath();
        var preferredDatabaseName = _currentDatabaseStateStore.GetCurrentDatabaseName(configPath);
        if (!TryLoadConfigurationSnapshot(configPath, preferredDatabaseName, out var snapshot, out _))
        {
            throw new InvalidOperationException(
                $"无法加载配置文件: {configPath}\n\n" +
                "请检查:\n" +
                "  1. 文件是否存在\n" +
                "  2. JSON 格式是否正确\n" +
                "  3. 文件编码是否为 UTF-8\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        lock (_stateLock)
        {
            _configPath = configPath;
            ApplyConfigurationSnapshot(snapshot);
            _logger.LogInformation("已从配置文件加载 {Count} 个数据库连接", _connections.Count);
        }

        if (!string.IsNullOrWhiteSpace(preferredDatabaseName) &&
            !string.Equals(preferredDatabaseName, snapshot.CurrentDatabaseName, StringComparison.Ordinal))
        {
            _currentDatabaseStateStore.SaveCurrentDatabaseName(configPath, snapshot.CurrentDatabaseName);
        }
    }

    private string GetConfiguredPath()
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
        return configPath;
    }

    private bool TryLoadConfigurationSnapshot(
        string configPath,
        string? preferredDatabaseName,
        out ConfigurationSnapshot snapshot,
        out string errorMessage)
    {
        if (!File.Exists(configPath))
        {
            _logger.LogError("配置文件不存在: {Path}", configPath);
            snapshot = ConfigurationSnapshot.Empty;
            errorMessage = $"配置文件不存在: {configPath}";
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
                snapshot = ConfigurationSnapshot.Empty;
                errorMessage = $"配置文件中未找到有效的数据库配置: {configPath}";
                return false;
            }

            var connections = new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal);
            string? defaultDatabaseName = null;
            foreach (var db in config.Databases)
            {
                connections[db.Name] = db;
                if (db.IsDefault && defaultDatabaseName == null)
                {
                    defaultDatabaseName = db.Name;
                }
            }

            var currentDatabaseName = preferredDatabaseName is not null && connections.ContainsKey(preferredDatabaseName)
                ? preferredDatabaseName
                : defaultDatabaseName ?? connections.Keys.First();

            snapshot = new ConfigurationSnapshot(configPath, connections, currentDatabaseName);
            _logger.LogInformation("成功从配置文件加载 {Count} 个数据库连接: {Path}", config.Databases.Count, configPath);
            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "配置文件 JSON 格式错误: {Path}", configPath);
            snapshot = ConfigurationSnapshot.Empty;
            errorMessage = $"配置文件 JSON 格式错误: {configPath}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法加载配置文件: {Path}", configPath);
            snapshot = ConfigurationSnapshot.Empty;
            errorMessage = $"无法加载配置文件: {configPath}";
            return false;
        }
    }

    private void ApplyConfigurationSnapshot(ConfigurationSnapshot snapshot)
    {
        _connections.Clear();
        foreach (var connection in snapshot.Connections)
        {
            _connections[connection.Key] = connection.Value;
        }

        _currentDatabaseName = snapshot.CurrentDatabaseName;
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

    private DatabaseConnection GetCurrentConnectionUnsafe()
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

    private DatabaseConnection GetConnectionUnsafe(string databaseName)
    {
        if (!_connections.TryGetValue(databaseName, out var connection))
        {
            throw new InvalidOperationException($"数据库连接 '{databaseName}' 不存在。可用的连接: {string.Join(", ", _connections.Keys)}");
        }

        return connection;
    }

    public string GetConnectionString()
    {
        lock (_stateLock)
        {
            return GetCurrentConnectionUnsafe().ConnectionString;
        }
    }

    public string GetDatabaseType()
    {
        lock (_stateLock)
        {
            return GetCurrentConnectionUnsafe().DbType;
        }
    }

    public DbType GetParsedDbType()
    {
        lock (_stateLock)
        {
            return _databaseHelper.ParseDbType(GetCurrentConnectionUnsafe().DbType);
        }
    }

    public ISqlSugarClient CreateClient()
    {
        lock (_stateLock)
        {
            return _clientFactory.CreateClient(GetCurrentConnectionUnsafe());
        }
    }

    public ISqlSugarClient CreateClient(string databaseName)
    {
        lock (_stateLock)
        {
            return _clientFactory.CreateClient(GetConnectionUnsafe(databaseName));
        }
    }

    public Task<ISqlSugarClient> CreateClientAsync()
    {
        return Task.FromResult(CreateClient());
    }

    public bool ValidateConfiguration()
    {
        try
        {
            lock (_stateLock)
            {
                _ = GetCurrentConnectionUnsafe().ConnectionString;
                _ = _databaseHelper.ParseDbType(GetCurrentConnectionUnsafe().DbType);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public string GetConfigurationSummary()
    {
        DatabaseConnection connection;
        int totalDatabases;
        lock (_stateLock)
        {
            connection = GetCurrentConnectionUnsafe();
            totalDatabases = _connections.Count;
        }

        return _resultSerializer.Serialize(new
        {
            configured = true,
            mode = totalDatabases > 1 ? "multi-database" : "single-database",
            totalDatabases,
            currentDatabase = connection.Name,
            databaseType = connection.DbType,
            description = connection.Description,
            connectionString = ConnectionStringMasker.Mask(connection.ConnectionString),
            message = "配置有效"
        });
    }

    public List<DatabaseConnection> GetAllConnections()
    {
        lock (_stateLock)
        {
            return _connections.Values.ToList();
        }
    }

    public bool SwitchDatabase(string databaseName)
    {
        string currentDatabaseName;
        lock (_stateLock)
        {
            if (!_connections.ContainsKey(databaseName))
            {
                _logger.LogWarning("尝试切换到不存在的数据库: {Name}", databaseName);
                return false;
            }

            _currentDatabaseName = databaseName;
            currentDatabaseName = _currentDatabaseName;
            _logger.LogInformation("已切换到数据库: {Name}", databaseName);
        }

        _currentDatabaseStateStore.SaveCurrentDatabaseName(_configPath, currentDatabaseName);
        return true;
    }

    public string GetCurrentDatabaseName()
    {
        lock (_stateLock)
        {
            return GetCurrentConnectionUnsafe().Name;
        }
    }

    public ConfigurationReloadResult ReloadConfiguration()
    {
        string previousDatabaseName;
        int previousDatabaseCount;
        lock (_stateLock)
        {
            previousDatabaseName = GetCurrentConnectionUnsafe().Name;
            previousDatabaseCount = _connections.Count;
        }

        string configPath;
        try
        {
            configPath = GetConfiguredPath();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取数据库配置路径失败，保留现有配置");
            return CreateReloadFailureResult(ex.Message, string.Empty, previousDatabaseName, previousDatabaseCount);
        }

        if (!TryLoadConfigurationSnapshot(configPath, previousDatabaseName, out var snapshot, out var errorMessage))
        {
            _logger.LogWarning("重新加载数据库配置失败，保留现有配置: {Path}", configPath);
            return CreateReloadFailureResult($"刷新配置失败，保留现有配置。{errorMessage}", configPath, previousDatabaseName, previousDatabaseCount);
        }

        string currentDatabaseName;
        int totalDatabases;
        lock (_stateLock)
        {
            ApplyConfigurationSnapshot(snapshot);
            _configPath = configPath;
            _clientFactory.ResetClientPool();
            currentDatabaseName = _currentDatabaseName ?? snapshot.CurrentDatabaseName;
            totalDatabases = _connections.Count;
        }

        _currentDatabaseStateStore.SaveCurrentDatabaseName(configPath, currentDatabaseName);

        var preservedCurrentDatabase = string.Equals(previousDatabaseName, currentDatabaseName, StringComparison.Ordinal);
        var message = preservedCurrentDatabase
            ? $"已重新加载数据库配置，并保留当前数据库 '{currentDatabaseName}'"
            : $"已重新加载数据库配置，当前数据库已从 '{previousDatabaseName}' 切换到 '{currentDatabaseName}'";

        _logger.LogInformation(
            "数据库配置刷新成功: {Path}，当前数据库 {Previous} -> {Current}",
            configPath,
            previousDatabaseName,
            currentDatabaseName);

        return new ConfigurationReloadResult
        {
            Success = true,
            Message = message,
            ConfigPath = configPath,
            PreviousDatabase = previousDatabaseName,
            CurrentDatabase = currentDatabaseName,
            TotalDatabases = totalDatabases,
            PreservedCurrentDatabase = preservedCurrentDatabase
        };
    }

    private static ConfigurationReloadResult CreateReloadFailureResult(
        string message,
        string configPath,
        string currentDatabaseName,
        int totalDatabases)
    {
        return new ConfigurationReloadResult
        {
            Success = false,
            Message = message,
            ConfigPath = configPath,
            PreviousDatabase = currentDatabaseName,
            CurrentDatabase = currentDatabaseName,
            TotalDatabases = totalDatabases,
            PreservedCurrentDatabase = true
        };
    }

    private sealed record ConfigurationSnapshot(
        string ConfigPath,
        Dictionary<string, DatabaseConnection> Connections,
        string CurrentDatabaseName)
    {
        public static ConfigurationSnapshot Empty { get; } =
            new(string.Empty, new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal), string.Empty);
    }
}
