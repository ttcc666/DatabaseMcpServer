using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using System.Text;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

internal sealed class JsonResultSerializer : IJsonResultSerializer
{
    public string Serialize(object data)
    {
        // Bounded results already contain the final budgeted JSON; avoid copying it through another writer.
        if (data is BoundedJsonResult encoded)
        {
            return Encoding.UTF8.GetString(encoded.Utf8Json);
        }

        return JsonSerializer.Serialize(data, JsonSerializationDefaults.IndentedCamelCase);
    }
}
