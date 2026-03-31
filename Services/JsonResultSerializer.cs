using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

internal sealed class JsonResultSerializer : IJsonResultSerializer
{
    public string Serialize(object data)
    {
        return JsonSerializer.Serialize(data, JsonSerializationDefaults.IndentedCamelCase);
    }
}
