using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Services;

internal sealed class CurrentDatabaseStateStore : ICurrentDatabaseStateStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<CurrentDatabaseStateStore> _logger;
    private readonly bool _enabled;
    private readonly string? _stateFilePath;
    private readonly object _stateLock = new();

    public CurrentDatabaseStateStore(
        ILogger<CurrentDatabaseStateStore> logger,
        bool enabled,
        string? stateFilePath = null)
    {
        _logger = logger;
        _enabled = enabled;
        _stateFilePath = enabled ? ResolveStateFilePath(stateFilePath) : null;
    }

    public string? GetCurrentDatabaseName(string configPath)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_stateFilePath))
        {
            return null;
        }

        var normalizedConfigPath = NormalizePath(configPath);

        lock (_stateLock)
        {
            var state = LoadStateUnsafe();
            var entry = state.Entries.FirstOrDefault(item =>
                string.Equals(item.ConfigPath, normalizedConfigPath, StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(entry?.CurrentDatabase)
                ? null
                : entry.CurrentDatabase;
        }
    }

    public void SaveCurrentDatabaseName(string configPath, string databaseName)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_stateFilePath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        var normalizedConfigPath = NormalizePath(configPath);

        lock (_stateLock)
        {
            var state = LoadStateUnsafe();
            var entry = state.Entries.FirstOrDefault(item =>
                string.Equals(item.ConfigPath, normalizedConfigPath, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                state.Entries.Add(new CurrentDatabaseStateEntry
                {
                    ConfigPath = normalizedConfigPath,
                    CurrentDatabase = databaseName,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                entry.CurrentDatabase = databaseName;
                entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            SaveStateUnsafe(state);
        }
    }

    private CurrentDatabaseStateDocument LoadStateUnsafe()
    {
        if (string.IsNullOrWhiteSpace(_stateFilePath) || !File.Exists(_stateFilePath))
        {
            return new CurrentDatabaseStateDocument();
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var document = JsonSerializer.Deserialize<CurrentDatabaseStateDocument>(json, ReadOptions);
            return document ?? new CurrentDatabaseStateDocument();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "CLI 当前数据库状态文件 JSON 格式错误，将重建状态文件: {Path}", _stateFilePath);
            return new CurrentDatabaseStateDocument();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 CLI 当前数据库状态文件失败，忽略旧状态: {Path}", _stateFilePath);
            return new CurrentDatabaseStateDocument();
        }
    }

    private void SaveStateUnsafe(CurrentDatabaseStateDocument state)
    {
        if (string.IsNullOrWhiteSpace(_stateFilePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(state, JsonSerializationDefaults.IndentedCamelCase);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存 CLI 当前数据库状态文件失败: {Path}", _stateFilePath);
        }
    }

    private static string ResolveStateFilePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("无法确定用户目录，无法持久化 CLI 当前数据库状态。");
        }

        return Path.Combine(userProfile, ".database-mcp", "cli-state.json");
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private sealed class CurrentDatabaseStateDocument
    {
        public List<CurrentDatabaseStateEntry> Entries { get; set; } = [];
    }

    private sealed class CurrentDatabaseStateEntry
    {
        public string ConfigPath { get; set; } = string.Empty;

        public string CurrentDatabase { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
