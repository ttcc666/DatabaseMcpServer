using System.Text.Encodings.Web;
using System.Text.Json;

namespace DatabaseMcpServer.Helpers;

internal static class JsonSerializationDefaults
{
    public static JsonSerializerOptions IndentedCamelCase { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
