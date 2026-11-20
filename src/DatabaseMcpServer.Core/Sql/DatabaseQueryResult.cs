using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed record DatabaseQueryResult
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public string? DatabaseType { get; init; }
    public long? ConfigurationVersion { get; init; }
    public int RowCount { get; init; }
    public List<Dictionary<string, JsonElement>> Data { get; init; } = [];
    public bool LimitReached { get; init; }
    public bool? Truncated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
