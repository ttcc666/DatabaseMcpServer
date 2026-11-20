using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

internal sealed class ConfigurationCommandResult
{
    public bool Success { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, object?> Details { get; init; } = [];
}
