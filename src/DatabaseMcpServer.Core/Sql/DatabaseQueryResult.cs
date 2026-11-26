using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed record DatabaseQueryResult : DatabaseQueryResult<Dictionary<string, JsonElement>>;

public record DatabaseQueryResult<TRow>
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public string? DatabaseType { get; init; }
    public long? ConfigurationVersion { get; init; }
    public int RowCount { get; init; }
    public List<TRow> Data { get; init; } = [];
    public bool LimitReached { get; init; }
    public bool? Truncated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}
