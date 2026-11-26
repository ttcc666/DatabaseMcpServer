using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed class TablePageResult
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public long? ConfigurationVersion { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public int? RowCount { get; init; }
    public List<Dictionary<string, JsonElement>>? Data { get; init; }
    public string[]? OrderBy { get; init; }
    public TableOrder[]? EffectiveOrderBy { get; init; }
    public bool? MayHaveMore { get; init; }
    public string? NextCursor { get; init; }
    public string? PagingMode { get; init; }
    public string? Consistency { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}
