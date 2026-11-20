using System.Globalization;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Models;

internal sealed record DatabaseExecutionOptions(int MaxConcurrent = 8, int MaxQueued = 32, int QueueTimeoutSeconds = 30)
{
    public bool BoundedQueryDefaults { get; init; }
    public int DefaultQueryRows { get; init; } = 1000;
    public int DefaultResponseBytes { get; init; } = 1024 * 1024;
    public int DefaultCommandTimeoutSeconds { get; init; } = 30;
    public int MaxQueryRows { get; init; } = 100_000;
    public int MaxResponseBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxCommandTimeoutSeconds { get; init; } = 86400;
    public bool AllowInfiniteTimeout { get; init; } = true;
    public DatabaseToolAccess Access { get; init; } = DatabaseToolAccess.Schema;
    public int MetadataCacheSeconds { get; init; } = 30;
    public int MetadataCacheEntries { get; init; } = 128;

    public void Validate()
    {
        if (MaxConcurrent < 1 || MaxQueued < 0 || QueueTimeoutSeconds < 1 || DefaultQueryRows < 1 || MaxQueryRows < 1 ||
            DefaultResponseBytes < 256 || MaxResponseBytes < 256 || DefaultCommandTimeoutSeconds < 1 || MaxCommandTimeoutSeconds < 1 ||
            MaxCommandTimeoutSeconds > SqlCommandTimeout.MaxTimeoutSeconds || MetadataCacheSeconds < 0 || MetadataCacheEntries < 1 || !Enum.IsDefined(Access))
            throw new ArgumentOutOfRangeException(nameof(DatabaseExecutionOptions), "数据库执行策略无效。");
    }

    public static DatabaseExecutionOptions FromEnvironment() => new(
        Read("DB_MAX_CONCURRENT", 8, 1, 1024), Read("DB_MAX_QUEUED", 32, 0, 10000),
        Read("DB_QUEUE_TIMEOUT_SECONDS", 30, 1, 86400))
    {
        BoundedQueryDefaults = ReadBoolean("DB_QUERY_BOUNDED_DEFAULTS", false),
        DefaultQueryRows = Read("DB_QUERY_DEFAULT_ROWS", 1000, 1, 1_000_000),
        DefaultResponseBytes = Read("DB_QUERY_DEFAULT_BYTES", 1024 * 1024, 256, 64 * 1024 * 1024),
        DefaultCommandTimeoutSeconds = Read("DB_QUERY_DEFAULT_TIMEOUT_SECONDS", 30, 1, 86400),
        MaxQueryRows = Read("DB_QUERY_MAX_ROWS", 100_000, 1, 1_000_000),
        MaxResponseBytes = Read("DB_QUERY_MAX_BYTES", 16 * 1024 * 1024, 256, 64 * 1024 * 1024),
        MaxCommandTimeoutSeconds = Read("DB_COMMAND_MAX_TIMEOUT_SECONDS", 86400, 1, 86400),
        AllowInfiniteTimeout = ReadBoolean("DB_ALLOW_INFINITE_TIMEOUT", true),
        Access = DatabaseAccessPolicy.Parse(Environment.GetEnvironmentVariable("DB_TOOL_ACCESS")),
        MetadataCacheSeconds = Read("DB_METADATA_CACHE_SECONDS", 30, 0, 3600),
        MetadataCacheEntries = Read("DB_METADATA_CACHE_ENTRIES", 128, 1, 4096)
    };

    public QueryBudget ResolveQuery(int? rows, int? bytes, int? timeout, bool bounded = false)
    {
        if (rows is <= 0 || rows > MaxQueryRows || bytes is < 256 || bytes > MaxResponseBytes)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "maxRows 或 maxResponseBytes 超出服务端允许范围。");
        if (bounded || BoundedQueryDefaults)
        {
            rows ??= Math.Min(DefaultQueryRows, MaxQueryRows);
            bytes ??= Math.Min(DefaultResponseBytes, MaxResponseBytes);
            timeout ??= Math.Min(DefaultCommandTimeoutSeconds, MaxCommandTimeoutSeconds);
        }
        return new(rows, bytes, ResolveTimeout(timeout));
    }

    public int? ResolveTimeout(int? timeout)
    {
        // The provider default is also subject to the configured server timeout ceiling.
        if (timeout == null && (BoundedQueryDefaults || MaxCommandTimeoutSeconds < 300))
            timeout = Math.Min(BoundedQueryDefaults ? DefaultCommandTimeoutSeconds : 300, MaxCommandTimeoutSeconds);
        if (timeout.HasValue) SqlCommandTimeout.Validate(timeout.Value);
        if (timeout > MaxCommandTimeoutSeconds || (timeout == 0 && !AllowInfiniteTimeout))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commandTimeoutSeconds 超出服务端策略；无限等待可能已被禁用。");
        return timeout;
    }

    private static bool ReadBoolean(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return bool.TryParse(value, out var parsed) ? parsed : throw new InvalidOperationException($"{name} 必须是 true/false。");
    }

    private static int Read(string name, int fallback, int min, int max)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max)
            return parsed;
        throw new InvalidOperationException($"{name} 必须在 {min}-{max} 之间。");
    }
}

internal sealed record QueryBudget(int? Rows, int? Bytes, int? TimeoutSeconds);
