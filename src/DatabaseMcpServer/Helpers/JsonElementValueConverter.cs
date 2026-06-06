using System.Text.Json;

namespace DatabaseMcpServer.Helpers;

internal static class JsonElementValueConverter
{
    public static object? ConvertToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertToValue).ToArray(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText()),
            _ => element.GetRawText()
        };
    }

    public static string? GetString(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    public static int GetInt32(IReadOnlyDictionary<string, JsonElement> values, string key, int defaultValue)
    {
        if (!values.TryGetValue(key, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    public static bool GetBoolean(IReadOnlyDictionary<string, JsonElement> values, string key, bool defaultValue)
    {
        if (!values.TryGetValue(key, out var element))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
