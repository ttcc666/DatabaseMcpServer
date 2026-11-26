namespace DatabaseMcpServer.Models;

public sealed class DatabaseObjectInfo
{
    public string Schema { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TableName { get; set; }
    public string Identity { get; set; } = "";
}

public sealed class ObjectListResult
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public long ConfigurationVersion { get; init; }
    public IReadOnlyList<DatabaseObjectInfo>? Objects { get; init; }
    public string? NextCursor { get; init; }
    public string PagingMode { get; init; } = "server";
    public string Consistency { get; init; } = "visible objects; no cross-page snapshot";
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}

public sealed class ObjectDefinitionResult
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public string? Kind { get; init; }
    public string? Name { get; init; }
    public string? Schema { get; init; }
    public string? Definition { get; init; }
    public string? Availability { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}
