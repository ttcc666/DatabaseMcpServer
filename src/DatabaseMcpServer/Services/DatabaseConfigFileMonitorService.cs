using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 长驻进程（MCP stdio / -web）可选监听 databases.json。
/// 默认关闭。优先级：启动参数 --enable-monitor-config > ENABLE_MONITOR_CONFIG > 文件内 enableMonitorConfig。
/// 启用后若文件中的默认库变化，则切换运行时当前库并刷新连接池。
/// </summary>
internal sealed class DatabaseConfigFileMonitorService : IHostedService, IDisposable
{
    private const int DebounceMilliseconds = 300;
    private const int ReadRetryCount = 5;
    private const int ReadRetryDelayMilliseconds = 50;

    private readonly IServiceProvider? _services;
    private readonly IDatabaseConfigService? _databaseConfig;
    private readonly DatabaseRuntimeOptions _runtimeOptions;
    private readonly ILogger<DatabaseConfigFileMonitorService> _logger;
    private readonly object _syncRoot = new();
    private FileSystemWatcher? _fileSystemWatcher;
    private System.Timers.Timer? _debounceTimer;
    private string? _monitoredPath;
    private string? _lastContentHash;
    private bool _disposed;

    [ActivatorUtilitiesConstructor]
    public DatabaseConfigFileMonitorService(
        IServiceProvider services,
        ILogger<DatabaseConfigFileMonitorService> logger,
        DatabaseRuntimeOptions runtimeOptions)
        : this(services, databaseConfig: null, logger, runtimeOptions)
    {
    }

    internal DatabaseConfigFileMonitorService(
        IDatabaseConfigService databaseConfig,
        ILogger<DatabaseConfigFileMonitorService> logger,
        DatabaseRuntimeOptions? runtimeOptions = null)
        : this(services: null, databaseConfig, logger, runtimeOptions)
    {
    }

    internal DatabaseConfigFileMonitorService(
        IServiceProvider services,
        ILogger<DatabaseConfigFileMonitorService> logger)
        : this(services, databaseConfig: null, logger, runtimeOptions: null)
    {
    }

    private DatabaseConfigFileMonitorService(
        IServiceProvider? services,
        IDatabaseConfigService? databaseConfig,
        ILogger<DatabaseConfigFileMonitorService> logger,
        DatabaseRuntimeOptions? runtimeOptions)
    {
        _services = services;
        _databaseConfig = databaseConfig;
        _runtimeOptions = runtimeOptions ?? DatabaseRuntimeOptions.Default;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TryStartMonitor();
        return Task.CompletedTask;
    }

    internal void EnsureMonitorStarted()
    {
        TryStartMonitor();
    }

    internal string? MonitoredPathForTests
    {
        get
        {
            lock (_syncRoot)
            {
                return _monitoredPath;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopMonitor();
        return Task.CompletedTask;
    }

    internal void HandleMonitoredFileChangedForTests()
    {
        ApplyMonitoredFileChange();
    }

    private void StartMonitor(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            _logger.LogWarning("无法监听配置文件（目录不存在）: {Path}", fullPath);
            return;
        }

        lock (_syncRoot)
        {
            StopMonitorUnsafe();

            _monitoredPath = fullPath;
            _lastContentHash = TryHashFile(fullPath);

            _fileSystemWatcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };
            _fileSystemWatcher.Changed += OnFileSystemEvent;
            _fileSystemWatcher.Created += OnFileSystemEvent;
            _fileSystemWatcher.Renamed += OnFileRenamed;

            _debounceTimer = new System.Timers.Timer(DebounceMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            _debounceTimer.Elapsed += OnDebounceElapsed;

            try
            {
                _fileSystemWatcher.EnableRaisingEvents = true;
                _logger.LogInformation("已开始监听数据库配置文件: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "启用配置文件监听失败，运行时将不会自动跟随文件变更: {Path}", fullPath);
                StopMonitorUnsafe();
            }
        }
    }

    private void StopMonitor()
    {
        lock (_syncRoot)
        {
            StopMonitorUnsafe();
        }
    }

    private void StopMonitorUnsafe()
    {
        if (_fileSystemWatcher != null)
        {
            _fileSystemWatcher.EnableRaisingEvents = false;
            _fileSystemWatcher.Changed -= OnFileSystemEvent;
            _fileSystemWatcher.Created -= OnFileSystemEvent;
            _fileSystemWatcher.Renamed -= OnFileRenamed;
            _fileSystemWatcher.Dispose();
            _fileSystemWatcher = null;
        }

        if (_debounceTimer != null)
        {
            _debounceTimer.Stop();
            _debounceTimer.Elapsed -= OnDebounceElapsed;
            _debounceTimer.Dispose();
            _debounceTimer = null;
        }

        _monitoredPath = null;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleApply(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleApply(e.FullPath);
    }

    private void ScheduleApply(string fullPath)
    {
        if (_disposed)
        {
            return;
        }

        string? monitoredPath;
        lock (_syncRoot)
        {
            monitoredPath = _monitoredPath;
            if (_debounceTimer == null || string.IsNullOrEmpty(monitoredPath))
            {
                return;
            }

            if (!string.Equals(Path.GetFileName(fullPath), Path.GetFileName(monitoredPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        ApplyMonitoredFileChange();
    }

    private void ApplyMonitoredFileChange()
    {
        if (_disposed)
        {
            return;
        }

        var config = TryGetDatabaseConfig();
        if (config == null)
        {
            _logger.LogDebug("监听到配置文件变化，但数据库配置服务尚未可用，稍后重试。");
            return;
        }

        if (!config.IsEnableMonitorConfigEnabled())
        {
            _logger.LogInformation("配置文件监听已关闭，停止跟随 databases.json 变更。");
            StopMonitor();
            return;
        }

        var configPath = config.GetConfigFilePath();
        var hash = TryHashFile(configPath);
        lock (_syncRoot)
        {
            if (hash != null && string.Equals(hash, _lastContentHash, StringComparison.Ordinal))
            {
                return;
            }
        }

        try
        {
            var result = config.ReloadConfiguration(followFileDefault: true);
            if (result.Success)
            {
                lock (_syncRoot)
                {
                    _lastContentHash = hash ?? TryHashFile(configPath);
                }

                if (result.PreservedCurrentDatabase)
                {
                    _logger.LogInformation(
                        "已从磁盘重新加载数据库配置并保留当前库 '{Current}'。",
                        result.CurrentDatabase);
                }
                else
                {
                    _logger.LogInformation(
                        "已从磁盘重新加载数据库配置，当前库从 '{Previous}' 切换到 '{Current}'。",
                        result.PreviousDatabase,
                        result.CurrentDatabase);
                }
            }
            else
            {
                _logger.LogWarning("监听到配置文件变化，但重新加载失败，保留现有配置: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理 databases.json 变更时失败，保留现有配置。");
        }

        if (!config.IsEnableMonitorConfigEnabled())
        {
            StopMonitor();
        }
    }

    private void TryStartMonitor()
    {
        if (_disposed)
        {
            return;
        }

        var config = TryGetDatabaseConfig();
        var enabled = config != null
            ? config.IsEnableMonitorConfigEnabled()
            : DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: false, _runtimeOptions.EnableMonitorConfig);

        if (!enabled)
        {
            _logger.LogDebug(
                "未启用 databases.json 文件监听。可通过 --enable-monitor-config true、{Env}=true 或配置文件 enableMonitorConfig=true 启用。",
                DatabaseConfigMonitorSettings.EnvironmentVariableName);
            return;
        }

        var path = !string.IsNullOrWhiteSpace(config?.GetConfigFilePath())
            ? config!.GetConfigFilePath()
            : ResolveIntendedConfigPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("已启用配置文件监听，但无法确定 databases.json 路径。");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        lock (_syncRoot)
        {
            if (_fileSystemWatcher != null
                && string.Equals(_monitoredPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        StartMonitor(fullPath);
    }

    private IDatabaseConfigService? TryGetDatabaseConfig()
    {
        if (_databaseConfig != null)
        {
            return _databaseConfig;
        }

        if (_services == null)
        {
            return null;
        }

        try
        {
            return _services.GetService<IDatabaseConfigService>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析数据库配置服务失败，暂按目标路径监听配置文件。");
            return null;
        }
    }

    private static string? ResolveIntendedConfigPath()
    {
        return CliConfigurationPathResolver.ResolveWritablePath(
            Environment.GetEnvironmentVariable("DB_CONFIG_PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Path;
    }

    private static string? TryHashFile(string path)
    {
        var content = TryReadAllText(path);
        if (content == null)
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string? TryReadAllText(string path)
    {
        for (var attempt = 0; attempt < ReadRetryCount; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.Sleep(ReadRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(ReadRetryDelayMilliseconds);
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitor();
    }
}
