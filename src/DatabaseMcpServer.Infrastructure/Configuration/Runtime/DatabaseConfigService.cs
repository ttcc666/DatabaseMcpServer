using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 协调配置加载、运行时发布与当前选择持久化。
/// </summary>
internal class DatabaseConfigService : IDatabaseConfigService
{
    private readonly ILogger<DatabaseConfigService> _logger;
    private readonly DatabaseConfigurationApplicationService _configurationEditor;
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly DatabaseRuntimeOptions _runtimeOptions;
    private readonly DatabaseConfigurationLoader _loader;
    private readonly DatabaseRuntimeState _state;
    private readonly object _selectionWriteLock = new();
    private readonly object _reloadLock = new();

    public DatabaseConfigService(
        ILogger<DatabaseConfigService> logger,
        IDatabaseHelperService databaseHelper,
        DatabaseExecutionCoordinator execution,
        DatabaseConfigurationApplicationService configurationEditor,
        ICurrentDatabaseStateStore currentDatabaseStateStore,
        DatabaseRuntimeOptions? runtimeOptions = null,
        string? userProfileDirectory = null,
        Func<string, string>? readConfig = null,
        ConfigurationChangeSignal? changes = null)
    {
        _logger = logger;
        _configurationEditor = configurationEditor;
        _currentDatabaseStateStore = currentDatabaseStateStore;
        _runtimeOptions = runtimeOptions ?? DatabaseRuntimeOptions.Default;
        _loader = new(logger, _runtimeOptions,
            userProfileDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            readConfig ?? ConfigFileReader.ReadAllText);

        if (_runtimeOptions.EnableMonitorConfig == true)
        {
            _logger.LogInformation("已通过启动参数 --enable-monitor-config 在本进程中启用配置文件监听。");
        }
        else if (_runtimeOptions.EnableMonitorConfig == false)
        {
            _logger.LogInformation("已通过启动参数 --enable-monitor-config false 在本进程中关闭配置文件监听。");
        }

        _state = new(_loader.GetConfiguredPath(), logger, databaseHelper, execution, _runtimeOptions, changes);
        LoadDatabaseConnections();
    }

    private void LoadDatabaseConnections()
    {
        var configPath = GetConfigFilePath();
        var preferredDatabaseName = _currentDatabaseStateStore.GetCurrentDatabaseName(configPath);
        if (!_loader.TryLoad(configPath, out var snapshot, out _))
        {
            if (_runtimeOptions.AllowUnconfigured)
            {
                _logger.LogWarning("配置不可用；保留 MCP 发现和配置诊断，数据库调用将返回配置错误。");
                return;
            }
            throw new InvalidOperationException(
                $"无法加载配置文件: {configPath}\n\n" +
                "请检查:\n" +
                "  1. 文件是否存在\n" +
                "  2. JSON 格式是否正确\n" +
                "  3. 文件编码是否为 UTF-8\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        var currentDatabaseName = _state.Initialize(snapshot, preferredDatabaseName);

        if (!string.IsNullOrWhiteSpace(preferredDatabaseName) &&
            !string.Equals(preferredDatabaseName, currentDatabaseName, StringComparison.Ordinal))
        {
            _currentDatabaseStateStore.SaveCurrentDatabaseName(configPath, currentDatabaseName);
        }
    }

    public string GetConnectionString() => _state.GetConnectionString();

    public string GetDatabaseType() => _state.GetDatabaseType();

    public DatabaseOperationReservation ReserveConnection(string? databaseName = null) => _state.ReserveConnection(databaseName);

    public bool ValidateConfiguration() => _state.ValidateConfiguration();

    public ConfigurationSummary GetConfigurationSummary() => _state.GetConfigurationSummary();

    public List<DatabaseConnection> GetAllConnections() => _state.GetAllConnections();

    public bool SwitchDatabase(string databaseName)
    {
        lock (_selectionWriteLock)
        {
            if (!_state.ContainsConnection(databaseName))
            {
                _logger.LogWarning("尝试切换到不存在的数据库: {Name}", databaseName);
                return false;
            }

            _currentDatabaseStateStore.SaveRequiredCurrentDatabaseName(GetConfigFilePath(), databaseName);
            _state.SetCurrentDatabase(databaseName);
            _logger.LogInformation("已切换到数据库: {Name}", databaseName);
            return true;
        }
    }

    public string GetCurrentDatabaseName() => _state.GetCurrentDatabaseName();

    public string GetConfigFilePath() => _state.GetConfigFilePath();

    public bool IsEnableMonitorConfigEnabled() => _state.IsEnableMonitorConfigEnabled();

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
            var configPath = GetConfigFilePath();
            return _configurationEditor.Edit(configPath, name, update, config =>
            {
                var renamed = update.NewName != null && update.NewName != name;
                var snapshot = _loader.CreateSavedSnapshot(configPath, config);
                var (previous, current) = _state.PublishEdit(snapshot, name, update);
                try
                {
                    bool? persisted = null;
                    if (renamed) persisted = _currentDatabaseStateStore.RenameCurrentDatabase(configPath, name, update.NewName!);
                    if (previous != current && !(renamed && previous == name && current == update.NewName))
                    {
                        _currentDatabaseStateStore.SaveRequiredCurrentDatabaseName(configPath, current);
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
                hash = _loader.ReadContentHash(GetConfigFilePath());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return _state.GetStatus(null, contentRead: false);
            }
            return _state.GetStatus(hash, contentRead: true);
        }
    }

    private ConfigurationReloadResult ReloadConfigurationCore(bool followFileDefault)
    {
        var configPath = GetConfigFilePath();

        if (!_loader.TryLoad(configPath, out var snapshot, out var errorMessage))
        {
            _logger.LogWarning("重新加载数据库配置失败，保留现有配置: {Path}", configPath);
            return _state.CreateReloadFailureResult($"刷新配置失败，保留现有配置。{errorMessage}", configPath);
        }

        string previousDatabaseName;
        string currentDatabaseName;
        int totalDatabases;
        string? persistenceError = null;
        lock (_selectionWriteLock)
        {
            (previousDatabaseName, currentDatabaseName, totalDatabases) = _state.PublishReload(snapshot, followFileDefault);

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
}
