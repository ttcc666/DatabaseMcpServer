using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库配置服务，负责加载连接配置并维护当前活动连接。
/// </summary>
internal class DatabaseConfigService : IDatabaseConfigService
{
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly DatabaseExecutionCoordinator _execution;
    private readonly DatabaseConfigurationApplicationService _configurationEditor;
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly DatabaseRuntimeOptions _runtimeOptions;
    private readonly string _userProfileDirectory;
    private readonly Func<string, string> _readConfig;
    private readonly object _stateLock = new();
    private readonly object _selectionWriteLock = new();
    private readonly object _reloadLock = new();
    private Dictionary<string, DatabaseConnection> _connections = new(StringComparer.Ordinal);
    private string _configPath = string.Empty;
    private string? _currentDatabaseName;
    private string? _defaultDatabaseName;
    private bool _enableMonitorConfig;
    private string? _appliedHash;
    private string? _observedHash;
    private long _version;
    private long _runtimeVersion;
    private long _savedVersion;

    public DatabaseConfigService(
        ILogger<DatabaseConfigService> logger,
        IDatabaseHelperService databaseHelper,
        DatabaseExecutionCoordinator execution,
        DatabaseConfigurationApplicationService configurationEditor,
        ICurrentDatabaseStateStore currentDatabaseStateStore,
        DatabaseRuntimeOptions? runtimeOptions = null,
        string? userProfileDirectory = null,
        Func<string, string>? readConfig = null)
    {
        _logger = logger;
        _databaseHelper = databaseHelper;
        _execution = execution;
        _configurationEditor = configurationEditor;
        _currentDatabaseStateStore = currentDatabaseStateStore;
        _runtimeOptions = runtimeOptions ?? DatabaseRuntimeOptions.Default;
        _userProfileDirectory = userProfileDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _readConfig = readConfig ?? ConfigFileReader.ReadAllText;

        if (_runtimeOptions.EnableMonitorConfig == true)
        {
            _logger.LogInformation("已通过启动参数 --enable-monitor-config 在本进程中启用配置文件监听。");
        }
        else if (_runtimeOptions.EnableMonitorConfig == false)
        {
            _logger.LogInformation("已通过启动参数 --enable-monitor-config false 在本进程中关闭配置文件监听。");
        }

        _configPath = GetConfiguredPath();
        LoadDatabaseConnections();
    }

    private void LoadDatabaseConnections()
    {
        var configPath = _configPath;
        var preferredDatabaseName = _currentDatabaseStateStore.GetCurrentDatabaseName(configPath);
        if (!TryLoadConfigurationSnapshot(configPath, out var snapshot, out _))
        {
            throw new InvalidOperationException(
                $"无法加载配置文件: {configPath}\n\n" +
                "请检查:\n" +
                "  1. 文件是否存在\n" +
                "  2. JSON 格式是否正确\n" +
                "  3. 文件编码是否为 UTF-8\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        string currentDatabaseName;
        lock (_stateLock)
        {
            _execution.UpdateConnections(snapshot.Connections);
            currentDatabaseName = ApplyConfigurationSnapshot(snapshot, preferredDatabaseName);
            _logger.LogInformation("已从配置文件加载 {Count} 个数据库连接", _connections.Count);
        }

        if (!string.IsNullOrWhiteSpace(preferredDatabaseName) &&
            !string.Equals(preferredDatabaseName, currentDatabaseName, StringComparison.Ordinal))
        {
            _currentDatabaseStateStore.SaveCurrentDatabaseName(configPath, currentDatabaseName);
        }
    }

    private string GetConfiguredPath()
    {
        CheckDeprecatedEnvironmentVariables();

        if (_runtimeOptions.ConfigPath is { } configPath)
        {
            return Path.GetFullPath(configPath);
        }

        var environmentConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var resolution = ConfigurationPathResolver.ResolveForMcpStdio(
            environmentConfigPath,
            _userProfileDirectory);

        if (!resolution.Success)
        {
            if (string.Equals(resolution.Source, "DB_CONFIG_PATH", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(resolution.ErrorMessage))
            {
                throw new InvalidOperationException(resolution.ErrorMessage);
            }

            throw new InvalidOperationException(
                "未找到数据库配置文件。请设置 DB_CONFIG_PATH 环境变量指向 databases.json 配置文件，\n" +
                "或在用户目录下创建 %USERPROFILE%/.database-mcp/databases.json。\n\n" +
                "示例:\n" +
                "  DB_CONFIG_PATH=D:\\config\\databases.json\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        _logger.LogInformation("使用配置文件路径: {Path} (来源: {Source})", resolution.Path, resolution.Source);
        return resolution.Path!;
    }

    private bool TryLoadConfigurationSnapshot(
        string configPath,
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
            var json = _readConfig(configPath);
            var config = JsonSerializer.Deserialize<DatabasesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var validation = DatabaseConfigurationValidator.Validate(config, requireConnections: true);
            foreach (var warning in validation.Warnings) _logger.LogWarning("配置诊断: {Warning}", warning);
            if (validation.Errors.Count > 0)
            {
                _logger.LogError("配置文件中未找到有效的数据库配置: {Path}", configPath);
                snapshot = ConfigurationSnapshot.Empty;
                errorMessage = string.Join("\n", validation.Errors);
                return false;
            }

            var connections = new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal);
            string? defaultDatabaseName = null;
            foreach (var db in config!.Databases)
            {
                connections[db.Name] = db;
                if (db.IsDefault && defaultDatabaseName == null)
                {
                    defaultDatabaseName = db.Name;
                }
            }

            snapshot = new ConfigurationSnapshot(
                configPath,
                connections,
                defaultDatabaseName,
                config.EnableMonitorConfig,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
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

    private string ApplyConfigurationSnapshot(ConfigurationSnapshot snapshot, string? preferredDatabaseName)
    {
        _connections = snapshot.Connections;
        _appliedHash = snapshot.ContentHash;
        if (_observedHash != _appliedHash || _version == 0)
        {
            _observedHash = _appliedHash;
            _savedVersion = ++_version;
        }
        _runtimeVersion = _savedVersion;
        _configPath = snapshot.ConfigPath;
        _defaultDatabaseName = snapshot.DefaultDatabaseName;
        _enableMonitorConfig = snapshot.EnableMonitorConfig;
        _currentDatabaseName = preferredDatabaseName != null && _connections.ContainsKey(preferredDatabaseName)
            ? preferredDatabaseName
            : _defaultDatabaseName ?? _connections.Keys.First();
        return _currentDatabaseName;
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

    public DatabaseOperationReservation ReserveConnection(string? databaseName = null)
    {
        lock (_stateLock)
        {
            if (databaseName != null && string.IsNullOrWhiteSpace(databaseName))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "databaseName 不能为空。");
            var connection = (databaseName == null ? GetCurrentConnectionUnsafe() : GetConnectionUnsafe(databaseName)).Copy();
            connection.ConfigurationVersion = _runtimeVersion;
            return _execution.Reserve(connection);
        }
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

    public ConfigurationSummary GetConfigurationSummary()
    {
        DatabaseConnection connection;
        int totalDatabases;
        bool enableMonitorConfig;
        bool enableMonitorConfigEnabled;
        lock (_stateLock)
        {
            connection = GetCurrentConnectionUnsafe();
            totalDatabases = _connections.Count;
            enableMonitorConfig = _enableMonitorConfig;
            enableMonitorConfigEnabled = DatabaseConfigMonitorSettings.IsEnabled(
                _enableMonitorConfig,
                _runtimeOptions.EnableMonitorConfig);
        }

        return new ConfigurationSummary
        {
            Configured = true,
            Mode = totalDatabases > 1 ? "multi-database" : "single-database",
            TotalDatabases = totalDatabases,
            CurrentDatabase = connection.Name,
            DatabaseType = connection.DbType,
            Description = connection.Description,
            ConnectionString = ConnectionStringMasker.Mask(connection.ConnectionString),
            EnableDangerousOperations = connection.EnableDangerousOperations,
            EnableMonitorConfigEnabled = enableMonitorConfigEnabled,
            EnableMonitorConfig = enableMonitorConfig,
            Message = "配置有效"
        };
    }

    public List<DatabaseConnection> GetAllConnections()
    {
        lock (_stateLock)
        {
            return _connections.Values.Select(connection => connection.Copy()).ToList();
        }
    }

    public bool SwitchDatabase(string databaseName)
    {
        lock (_selectionWriteLock)
        {
            lock (_stateLock)
            {
                if (!_connections.ContainsKey(databaseName))
                {
                    _logger.LogWarning("尝试切换到不存在的数据库: {Name}", databaseName);
                    return false;
                }
            }

            _currentDatabaseStateStore.SaveRequiredCurrentDatabaseName(_configPath, databaseName);
            lock (_stateLock)
            {
                _currentDatabaseName = databaseName;
            }
            _logger.LogInformation("已切换到数据库: {Name}", databaseName);
            return true;
        }
    }

    public string GetCurrentDatabaseName()
    {
        lock (_stateLock)
        {
            return GetCurrentConnectionUnsafe().Name;
        }
    }

    public string GetConfigFilePath()
    {
        lock (_stateLock)
        {
            return _configPath;
        }
    }

    public bool IsEnableMonitorConfigEnabled()
    {
        lock (_stateLock)
        {
            return DatabaseConfigMonitorSettings.IsEnabled(_enableMonitorConfig, _runtimeOptions.EnableMonitorConfig);
        }
    }

    public ConfigurationReloadResult ReloadConfiguration(bool followFileDefault = false)
    {
        lock (_reloadLock)
        {
            return ReloadConfigurationCore(followFileDefault);
        }
    }

    public ConfigurationMutationResult RenameDatabase(string name, string newName)
        => UpdateDatabase(name, new DatabaseConnectionUpdate { NewName = newName });

    public ConfigurationMutationResult UpdateDatabase(string name, DatabaseConnectionUpdate update)
    {
        lock (_reloadLock)
        lock (_selectionWriteLock)
        {
            return _configurationEditor.Edit(_configPath, name, update, config =>
            {
                var renamed = update.NewName != null && update.NewName != name;
                var snapshot = new ConfigurationSnapshot(_configPath,
                    config.Databases.ToDictionary(db => db.Name, StringComparer.Ordinal),
                    config.Databases.FirstOrDefault(db => db.IsDefault)?.Name, config.EnableMonitorConfig,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_readConfig(_configPath)))));
                string previous;
                string current;
                lock (_stateLock)
                {
                    previous = GetCurrentConnectionUnsafe().Name;
                    var preferred = renamed && previous == name ? update.NewName : previous;
                    if (update.ApplySetDefault && IsEnableMonitorConfigEnabled() && snapshot.DefaultDatabaseName != _defaultDatabaseName)
                        preferred = snapshot.DefaultDatabaseName ?? preferred;
                    _execution.UpdateConnections(snapshot.Connections, renamed ? name : null, renamed ? update.NewName : null);
                    current = ApplyConfigurationSnapshot(snapshot, preferred);
                }
                try
                {
                    bool? persisted = null;
                    if (renamed) persisted = _currentDatabaseStateStore.RenameCurrentDatabase(_configPath, name, update.NewName!);
                    if (previous != current && !(renamed && previous == name && current == update.NewName))
                    {
                        _currentDatabaseStateStore.SaveRequiredCurrentDatabaseName(_configPath, current);
                        persisted = true;
                    }
                    var success = previous == current || persisted != false;
                    return new(success, true, true, persisted,
                        success ? $"已更新数据库连接 '{update.NewName ?? name}'。" : "配置已生效，但持久化选择已被其他操作更改。", current);
                }
                catch (Exception ex) { return new(false, true, true, false, ex.Message, current); }
            });
        }
    }

    public DatabaseRuntimeStatus GetRuntimeStatus()
    {
        lock (_reloadLock)
        {
            string? hash;
            try
            {
                hash = File.Exists(_configPath)
                    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_readConfig(_configPath)))) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_stateLock) return new(_currentDatabaseName, _runtimeVersion, _savedVersion, null);
            }
            lock (_stateLock)
            {
                if (_observedHash != hash)
                {
                    _observedHash = hash;
                    _savedVersion = ++_version;
                }
                return new(_currentDatabaseName, _runtimeVersion, _savedVersion, hash != _appliedHash);
            }
        }
    }

    private ConfigurationReloadResult ReloadConfigurationCore(bool followFileDefault)
    {
        var configPath = _configPath;

        if (!TryLoadConfigurationSnapshot(configPath, out var snapshot, out var errorMessage))
        {
            _logger.LogWarning("重新加载数据库配置失败，保留现有配置: {Path}", configPath);
            return CreateReloadFailureResult($"刷新配置失败，保留现有配置。{errorMessage}", configPath);
        }

        string previousDatabaseName;
        string currentDatabaseName;
        int totalDatabases;
        string? persistenceError = null;
        lock (_selectionWriteLock)
        {
            lock (_stateLock)
            {
                previousDatabaseName = GetCurrentConnectionUnsafe().Name;
                var useFileDefault = followFileDefault && !string.IsNullOrWhiteSpace(snapshot.DefaultDatabaseName)
                    && !string.Equals(_defaultDatabaseName, snapshot.DefaultDatabaseName, StringComparison.Ordinal);
                _execution.UpdateConnections(snapshot.Connections);
                currentDatabaseName = ApplyConfigurationSnapshot(snapshot, useFileDefault ? snapshot.DefaultDatabaseName : previousDatabaseName);
                totalDatabases = _connections.Count;
            }

            if (previousDatabaseName != currentDatabaseName)
            {
                try { _currentDatabaseStateStore.SaveRequiredCurrentDatabaseName(configPath, currentDatabaseName); }
                catch (Exception ex) { persistenceError = ex.Message; }
            }
        }

        var preservedCurrentDatabase = string.Equals(previousDatabaseName, currentDatabaseName, StringComparison.Ordinal);
        var message = preservedCurrentDatabase
            ? $"已重新加载数据库配置，并保留当前数据库 '{currentDatabaseName}'"
            : $"已重新加载数据库配置，当前数据库已从 '{previousDatabaseName}' 切换到 '{currentDatabaseName}'";

        _logger.LogInformation(
            "数据库配置刷新成功: {Path}，当前数据库 {Previous} -> {Current}，followFileDefault={FollowFileDefault}",
            configPath,
            previousDatabaseName,
            currentDatabaseName,
            followFileDefault);

        return new ConfigurationReloadResult
        {
            Success = persistenceError == null,
            RuntimeApplied = true,
            SelectionPersisted = previousDatabaseName == currentDatabaseName ? null : persistenceError == null,
            ContentHash = snapshot.ContentHash,
            Message = persistenceError == null ? message : $"{message}，但持久化选择失败：{persistenceError}",
            ConfigPath = configPath,
            PreviousDatabase = previousDatabaseName,
            CurrentDatabase = currentDatabaseName,
            TotalDatabases = totalDatabases,
            PreservedCurrentDatabase = preservedCurrentDatabase
        };
    }

    private ConfigurationReloadResult CreateReloadFailureResult(string message, string configPath)
    {
        lock (_stateLock)
        {
            return new ConfigurationReloadResult
            {
                Success = false,
                Message = message,
                ConfigPath = configPath,
                PreviousDatabase = _currentDatabaseName!,
                CurrentDatabase = _currentDatabaseName!,
                TotalDatabases = _connections.Count,
                PreservedCurrentDatabase = true
            };
        }
    }

    private sealed record ConfigurationSnapshot(
        string ConfigPath,
        Dictionary<string, DatabaseConnection> Connections,
        string? DefaultDatabaseName,
        bool EnableMonitorConfig,
        string? ContentHash = null)
    {
        public static ConfigurationSnapshot Empty { get; } =
            new(string.Empty, new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal), null, false);
    }
}
