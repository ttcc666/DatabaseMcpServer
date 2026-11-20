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

        try
        {
            var entry = LoadState(_stateFilePath).FindEntry(normalizedConfigPath);

            return string.IsNullOrWhiteSpace(entry?.CurrentDatabase)
                ? null
                : entry.CurrentDatabase;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "读取 CLI 当前数据库状态文件失败: {Path}", _stateFilePath);
            return null;
        }
    }

    public void SaveCurrentDatabaseName(string configPath, string databaseName)
    {
        try
        {
            SaveRequiredCurrentDatabaseName(configPath, databaseName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "保存 CLI 当前数据库状态失败，保留原状态文件: {Path}", _stateFilePath);
        }
    }

    public void SaveRequiredCurrentDatabaseName(string configPath, string databaseName)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_stateFilePath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("无法保存空的当前数据库名称。");
        }

        var normalizedConfigPath = NormalizePath(configPath);

        try
        {
            using var write = new AtomicJsonFileWrite(_stateFilePath);
            var state = LoadState(_stateFilePath);
            var entry = state.FindEntry(normalizedConfigPath);

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

            write.Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"无法保存当前数据库选择到 '{_stateFilePath}'。{ex.Message}", ex);
        }
    }

    public bool RenameCurrentDatabase(string configPath, string oldName, string newName)
    {
        if (!_enabled || _stateFilePath == null || !File.Exists(_stateFilePath)) return true;
        using var write = new AtomicJsonFileWrite(_stateFilePath);
        var state = LoadState(_stateFilePath);
        var entry = state.FindEntry(NormalizePath(configPath));
        if (entry == null) return true;
        if (entry.CurrentDatabase != oldName)
            return entry?.CurrentDatabase == newName;
        entry.CurrentDatabase = newName;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        write.Save(state);
        return true;
    }

    private static CurrentDatabaseStateDocument LoadState(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<CurrentDatabaseStateDocument>(ConfigFileReader.ReadAllText(path), ReadOptions);
            if (state?.Entries == null || state.Entries.Any(entry => entry == null))
            {
                throw new JsonException("CLI 状态缺少有效的 entries 列表。");
            }

            return state;
        }
        catch (FileNotFoundException)
        {
            return new CurrentDatabaseStateDocument();
        }
        catch (DirectoryNotFoundException)
        {
            return new CurrentDatabaseStateDocument();
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
        [System.Text.Json.Serialization.JsonRequired]
        public List<CurrentDatabaseStateEntry> Entries { get; set; } = [];

        public CurrentDatabaseStateEntry? FindEntry(string configPath) => Entries.FirstOrDefault(entry =>
            string.Equals(entry.ConfigPath, configPath, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal));
    }

    private sealed class CurrentDatabaseStateEntry
    {
        public string ConfigPath { get; set; } = string.Empty;

        public string CurrentDatabase { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
