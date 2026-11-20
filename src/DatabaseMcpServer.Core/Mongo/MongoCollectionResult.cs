using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

internal sealed class MongoCollectionResult
{
    public bool Success { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DatabaseName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ConfigurationVersion { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<JsonElement>? Documents { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<JsonElement>? Collections { get; set; }
    public int ReturnedCount { get; set; }
    public bool LimitReached { get; set; }
    public bool? Truncated { get; set; } = false;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MatchedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ModifiedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? DeletedCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? InsertedId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? UpsertedId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Acknowledged { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WriteOutcome { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ErrorCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ErrorMessage { get; set; }
}

internal enum MongoOperation { List, Find, Count, Insert, Update, Delete }

internal sealed record MongoCollectionRequest(MongoOperation Operation, string? Collection = null,
    Dictionary<string, JsonElement>? Filter = null, Dictionary<string, JsonElement>? Projection = null,
    Dictionary<string, JsonElement>? Sort = null, Dictionary<string, JsonElement>? Document = null,
    Dictionary<string, JsonElement>? Update = null, string? NamePrefix = null, int Skip = 0,
    bool Many = false, bool Upsert = false, bool AllowAll = false, int? MaxRows = null,
    int? MaxResponseBytes = null, string? DatabaseName = null, int? CommandTimeoutSeconds = null)
{
    public bool IsWrite => Operation is MongoOperation.Insert or MongoOperation.Update or MongoOperation.Delete;
}
