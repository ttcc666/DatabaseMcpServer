using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Helpers;

[JsonConverter(typeof(BoundedJsonResultConverter))]
internal sealed record BoundedJsonResult(byte[] Utf8Json);

internal sealed class BoundedJsonResultConverter : JsonConverter<BoundedJsonResult>
{
    public override BoundedJsonResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, BoundedJsonResult value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.Utf8Json, skipInputValidation: true);
}
