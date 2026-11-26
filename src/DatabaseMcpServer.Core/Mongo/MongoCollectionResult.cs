using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

internal sealed class MongoCollectionResult
{
    public bool Success { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DatabaseName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ConfigurationVersion { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<JsonElement>? Documents { get; set; }
    public int ReturnedCount { get; set; }
    public bool LimitReached { get; set; }
    public bool? Truncated { get; set; } = false;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? InsertedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ModifiedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? DeletedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Acknowledged { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WriteOutcome { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ErrorCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ErrorMessage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Retryable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phase { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Capability { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Hint { get; set; }
}

internal enum MongoOperation { Find, Aggregate, InsertMany, Update, Delete, Truncate }

internal sealed record MongoCollectionRequest(MongoOperation Operation, string? Collection = null,
    Dictionary<string, JsonElement>? Filter = null, Dictionary<string, JsonElement>? Projection = null,
    Dictionary<string, JsonElement>? Sort = null, Dictionary<string, JsonElement>[]? Documents = null,
    Dictionary<string, JsonElement>? Update = null, int Skip = 0,
    bool AllowAll = false, int? MaxRows = null,
    int? MaxResponseBytes = null, string? DatabaseName = null, int? CommandTimeoutSeconds = null,
    Dictionary<string, JsonElement>[]? Pipeline = null)
{
    public bool IsWrite => Operation is MongoOperation.InsertMany or MongoOperation.Update or MongoOperation.Delete or MongoOperation.Truncate;
}
