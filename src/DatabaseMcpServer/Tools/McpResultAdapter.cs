using System.Text.Json;
using DatabaseMcpServer.Helpers;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools;

internal static class McpResultAdapter
{
    private static readonly DatabaseMcpServer.Services.JsonResultSerializer Serializer = new();

    public static string Text(object result)
    {
        try { return DatabaseTelemetry.Serialize(() => Serializer.Serialize(result)); }
        catch (Exception ex)
        {
            return Serializer.Serialize(DatabaseMcpServer.Services.DatabaseErrorMapper.Map(ex,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        }
    }

    public static async Task<string> Text(Task<object> result) => Text(await result.ConfigureAwait(false));

    public static async Task<CallToolResult> Structured(Task<object> result)
    {
        var json = Text(await result.ConfigureAwait(false));
        return StructuredJson(json, !ToolResultStatus.IsFailure(json));
    }

    public static CallToolResult Structured(object result)
    {
        var json = Text(result);
        return StructuredJson(json, !ToolResultStatus.IsFailure(json));
    }

    public static CallToolResult Structured<T>(T result, bool success)
        => StructuredJson(JsonSerializer.Serialize(result, JsonSerializationDefaults.CompactCamelCase), success);

    public static CallToolResult StructuredJson(string json, bool success)
    {
        using var document = JsonDocument.Parse(json);
        return new() { IsError = !success, Content = [new TextContentBlock { Text = json }],
            StructuredContent = document.RootElement.Clone() };
    }
}
