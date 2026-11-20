using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Helpers;

// Config compatibility: accept JSON scalars; persist one canonical string representation.
internal sealed class OptimizationSettingsJsonConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("optimizationSettings 必须是对象。");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number => property.Value.GetRawText(),
                _ => throw new JsonException("optimizationSettings 选项必须为字符串、布尔值或数字。")
            };
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}
