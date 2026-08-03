using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DatabaseMcpServer.Cli;

internal static class CliToolInvoker
{
    public static object[] BindArguments(
        CliToolMetadata tool,
        IReadOnlyDictionary<string, string?> optionValues,
        bool rejectUnknownOptions = false)
    {
        if (rejectUnknownOptions)
        {
            var knownOptions = tool.Parameters
                .Select(parameter => parameter.OptionName)
                .ToHashSet(StringComparer.Ordinal);
            var unknownOption = optionValues.Keys.FirstOrDefault(option => !knownOptions.Contains(option));
            if (unknownOption != null)
            {
                throw new InvalidOperationException($"未知选项 '--{unknownOption}'。tool: {tool.Name}");
            }
        }

        var values = new object[tool.Parameters.Count];
        for (var i = 0; i < tool.Parameters.Count; i++)
        {
            var parameter = tool.Parameters[i];
            if (optionValues.TryGetValue(parameter.OptionName, out var rawValue))
            {
                values[i] = ConvertValue(parameter, rawValue);
                continue;
            }

            if (parameter.IsRequired)
            {
                throw new InvalidOperationException($"缺少必填选项 '--{parameter.OptionName}'。");
            }

            values[i] = parameter.DefaultValue!;
        }

        return values;
    }

    public static async Task<string> InvokeAsync(
        CliToolMetadata tool,
        object toolInstance,
        object[] arguments)
    {
        var result = tool.Method.Invoke(toolInstance, arguments);
        return result switch
        {
            string text => text,
            Task<string> task => await task,
            _ => throw new InvalidOperationException($"tool '{tool.Name}' 返回类型不受支持。")
        };
    }

    public static bool IsToolFailure(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("success", out var successElement) &&
                   successElement.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null }
            ? exception.InnerException
            : exception;
    }

    private static object ConvertValue(CliToolParameterMetadata parameter, string? rawValue)
    {
        var targetType = parameter.EffectiveType;

        if (targetType == typeof(string))
        {
            return rawValue ?? string.Empty;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return intValue;
            }

            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要 int 值。");
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(rawValue, out var boolValue))
            {
                return boolValue;
            }

            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要 bool 值。");
        }

        if (targetType == typeof(JsonElement))
        {
            try
            {
                return JsonDocument.Parse(rawValue ?? "null").RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要有效 JSON。{ex.Message}");
            }
        }

        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 使用了不支持的参数类型 '{targetType.Name}'。");
    }
}
