using System.Text.Json;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal static class JsonElementValueConverter
{
    public static object? ConvertToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ConvertNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertToValue).ToArray(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText()),
            _ => element.GetRawText()
        };
    }

    private static object ConvertNumber(JsonElement element)
    {
        // Return separately to avoid promotion from Int64/Decimal to Double before boxing.
        if (element.TryGetInt64(out var integer))
        {
            return integer;
        }

        // TryGetDecimal can succeed after rounding or underflowing to zero.
        if (element.TryGetDecimal(out var number) &&
            JsonElement.DeepEquals(element, JsonSerializer.SerializeToElement(number)))
        {
            return number;
        }

        var floatingPoint = element.GetDouble();
        if (double.IsFinite(floatingPoint) &&
            JsonElement.DeepEquals(element, JsonSerializer.SerializeToElement(floatingPoint)))
        {
            return floatingPoint;
        }

        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters,
            "数值参数超出可往返表示的精度或范围；请使用字符串并在 SQL 中显式转换为目标数据库类型。");
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
