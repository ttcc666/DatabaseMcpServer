using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Helpers;

internal static class ToolResultStatus
{
    public static bool IsFailure(string payload)
    {
        try
        {
            // Skip result data without building a second DOM for potentially large query results.
            return JsonSerializer.Deserialize<StatusEnvelope>(payload)?.Success == false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsFailure(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object &&
        result.TryGetProperty("success", out var success) &&
        success.ValueKind == JsonValueKind.False;

    private sealed class StatusEnvelope
    {
        [JsonPropertyName("success")]
        public bool? Success { get; set; }
    }
}
