using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

/// <summary>Owns the published configuration and its admission identity under one lock; performs no file I/O.</summary>
internal sealed class DatabaseRuntimeState
{
    private readonly object _stateLock = new();
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly DatabaseExecutionCoordinator _execution;
    private readonly DatabaseRuntimeOptions _runtimeOptions;
    private Dictionary<string, DatabaseConnection> _connections = new(StringComparer.Ordinal);
    private string _configPath = string.Empty;
    private string? _currentDatabaseName;
    private string? _defaultDatabaseName;
    private bool _enableMonitorConfig;
    private string? _appliedHash;
    private string? _configurationFingerprint;
    private string? _observedHash;
    private long _version;
    private long _runtimeVersion;
    private long _savedVersion;
    private readonly ConfigurationChangeSignal? _changes;

    public DatabaseRuntimeState(string configPath, ILogger<DatabaseConfigService> logger,
        IDatabaseHelperService databaseHelper, DatabaseExecutionCoordinator execution,
        DatabaseRuntimeOptions runtimeOptions, ConfigurationChangeSignal? changes)
    {
        _configPath = configPath;
        _logger = logger;
        _databaseHelper = databaseHelper;
        _execution = execution;
        _runtimeOptions = runtimeOptions;
        _changes = changes;
    }

    public string Initialize(DatabaseConfigurationSnapshot snapshot, string? preferredDatabaseName)
    {
        lock (_stateLock)
        {
            _execution.UpdateConnections(snapshot.Connections);
            var current = ApplyConfigurationSnapshot(snapshot, preferredDatabaseName);
            _logger.LogInformation("已从配置文件加载 {Count} 个数据库连接", _connections.Count);
            return current;
        }
    }

    public (string Previous, string Current, int Count) PublishReload(DatabaseConfigurationSnapshot snapshot, bool followFileDefault)
    {
        lock (_stateLock)
        {
            var previous = _connections.Count == 0 ? string.Empty : GetCurrentConnectionUnsafe().Name;
            var useFileDefault = followFileDefault && !string.IsNullOrWhiteSpace(snapshot.DefaultDatabaseName)
                && !string.Equals(_defaultDatabaseName, snapshot.DefaultDatabaseName, StringComparison.Ordinal);
            _execution.UpdateConnections(snapshot.Connections);
            var current = ApplyConfigurationSnapshot(snapshot, useFileDefault ? snapshot.DefaultDatabaseName : previous);
            return (previous, current, _connections.Count);
        }
    }

    public (string Previous, string Current) PublishEdit(DatabaseConfigurationSnapshot snapshot, string name, DatabaseConnectionUpdate update)
    {
        var renamed = update.NewName != null && update.NewName != name;
        lock (_stateLock)
        {
            var previous = GetCurrentConnectionUnsafe().Name;
            var preferred = renamed && previous == name ? update.NewName : previous;
            if (update.ApplySetDefault && IsEnableMonitorConfigEnabled() && snapshot.DefaultDatabaseName != _defaultDatabaseName)
                preferred = snapshot.DefaultDatabaseName ?? preferred;
            _execution.UpdateConnections(snapshot.Connections, renamed ? name : null, renamed ? update.NewName : null);
            var current = ApplyConfigurationSnapshot(snapshot, preferred);
            return (previous, current);
        }
    }

    private string ApplyConfigurationSnapshot(DatabaseConfigurationSnapshot snapshot, string? preferredDatabaseName)
    {
        _connections = snapshot.Connections;
        _appliedHash = snapshot.ContentHash;
        _configurationFingerprint = snapshot.ContentHash == null ? null
            : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Path = Path.GetFullPath(snapshot.ConfigPath), snapshot.ContentHash
            })));
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
        _changes?.Publish();
        return _currentDatabaseName;
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
            connection.ConfigurationFingerprint = _configurationFingerprint;
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

    public bool ContainsConnection(string databaseName)
    {
        lock (_stateLock)
        {
            return _connections.ContainsKey(databaseName);
        }
    }

    // The service serializes selection writes and publishes only after persistence succeeds.
    public void SetCurrentDatabase(string databaseName)
    {
        lock (_stateLock)
        {
            _currentDatabaseName = databaseName;
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

    public DatabaseRuntimeStatus GetStatus(string? contentHash, bool contentRead)
    {
        lock (_stateLock)
        {
            if (contentRead && _observedHash != contentHash)
            {
                _observedHash = contentHash;
                _savedVersion = ++_version;
            }
            return new(_currentDatabaseName, _runtimeVersion, _savedVersion,
                contentRead ? contentHash != _appliedHash : null);
        }
    }

    public ConfigurationReloadResult CreateReloadFailureResult(string message, string configPath)
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
}
