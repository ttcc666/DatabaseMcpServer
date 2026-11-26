using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

internal static class TestJson
{
    public static JsonDocument Parse(ModelContextProtocol.Protocol.CallToolResult result)
        => JsonDocument.Parse(((ModelContextProtocol.Protocol.TextContentBlock)Assert.Single(result.Content)).Text);
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json);
    public static JsonDocument Parse(Stream stream) => JsonDocument.Parse(stream);
    public static JsonDocument Parse(ConfigurationCommandResult result)
        => JsonDocument.Parse(JsonSerializer.Serialize(result, JsonSerializationDefaults.IndentedCamelCase));
}
