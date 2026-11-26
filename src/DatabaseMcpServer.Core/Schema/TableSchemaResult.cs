using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed class TableSchemaResult
{
    public bool Success { get; init; }
    public TableSchemaData? Data { get; init; }
    public List<TableSchemaEntry>? Tables { get; init; }
    public int? RowCount { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}

public sealed class TableSchemaEntry
{
    public string TableName { get; init; } = "";
    public string Status { get; init; } = "";
    public TableSchemaData? Data { get; init; }
    public int? ErrorCode { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
}

public sealed class TableSchemaData
{
    public string TableName { get; init; } = "";
    public List<Dictionary<string, JsonElement>>? Columns { get; init; }
    public string[]? PrimaryKeys { get; init; }
    public string[]? IdentityColumns { get; init; }
    public JsonElement? Indexes { get; init; }
    public SchemaSection? ForeignKeys { get; init; }
    public SchemaSection? Constraints { get; init; }
    public SchemaSection? Comments { get; init; }
}

public sealed class SchemaSection
{
    public string Status { get; init; } = "";
    public int? ErrorCode { get; init; }
    public string? Reason { get; init; }
    public List<Dictionary<string, JsonElement>>? Data { get; init; }
}
