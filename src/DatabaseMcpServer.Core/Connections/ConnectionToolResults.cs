using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed class ConnectionTestResult
{
    public bool Success { get; init; }
    public bool Connected { get; init; }
    public int? Attempts { get; init; }
    public string? CurrentDatabase { get; init; }
    public string? DatabaseType { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}

public sealed class ConnectionListResult
{
    public bool Success { get; init; }
    public int? TotalDatabases { get; init; }
    public string? CurrentDatabase { get; init; }
    public List<ConnectionListItem>? Databases { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}

public sealed class ConnectionListItem
{
    public string Name { get; init; } = "";
    public string DbType { get; init; } = "";
    public string? Description { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class DatabaseCapabilitiesResult
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public string? DatabaseType { get; init; }
    public long? ConfigurationVersion { get; init; }
    public string? AccessMode { get; init; }
    public Dictionary<string, string>? Capabilities { get; init; }
    public Dictionary<string, Dictionary<string, string>>? CapabilityDetails { get; init; }
    public Dictionary<string, string>? CapabilityVerification { get; init; }
    public Dictionary<string, JsonElement>? Budgets { get; init; }
    public Dictionary<string, JsonElement>? ConnectionOptions { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}
