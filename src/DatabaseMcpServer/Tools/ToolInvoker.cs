using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools;

internal static class ToolInvoker
{
    private static readonly JsonSerializerOptions ArgumentJson = new() { PropertyNameCaseInsensitive = true };

    public static object[] BindJsonArguments(ToolDescriptor tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var known = tool.Parameters.Select(parameter => parameter.OptionName).ToHashSet(StringComparer.Ordinal);
        var unknown = arguments.Keys.FirstOrDefault(name => !known.Contains(name));
        if (unknown != null)
            throw new InvalidOperationException($"未知选项 '--{unknown}'。tool: {tool.Name}");

        return tool.Parameters.Select(parameter =>
        {
            if (arguments.TryGetValue(parameter.OptionName, out var value))
                return ConvertJsonValue(parameter, value);
            if (parameter.IsRequired)
                throw new InvalidOperationException($"缺少必填选项 '--{parameter.OptionName}'。");
            return parameter.DefaultValue!;
        }).ToArray();
    }

    private static object ConvertJsonValue(ToolParameterDescriptor parameter, JsonElement value)
    {
        var type = parameter.EffectiveType;
        if (type == typeof(string))
        {
            if (value.ValueKind == JsonValueKind.Null) return string.Empty;
            if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要字符串值。");
        }
        if (type == typeof(int) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (type == typeof(bool) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        if (type == typeof(Dictionary<string, JsonElement>))
        {
            try
            {
                MongoDocumentContract.ValidateObject(value);
                return value.Deserialize<Dictionary<string, JsonElement>>()!;
            }
            catch (DatabaseMcpServer.Models.DatabaseMcpException ex) { throw new InvalidOperationException(ex.Message); }
            catch (JsonException) { throw new InvalidOperationException("参数需要 JSON / Extended JSON v2 对象。"); }
        }
        if (type.IsArray)
        {
            try { return value.Deserialize(type, ArgumentJson)!; }
            catch (JsonException ex) { throw new InvalidOperationException("参数需要类型化 JSON 数组。", ex); }
        }
        if (value.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要 {parameter.DisplayTypeName} 值。");
        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 的 JSON 类型不正确。");
    }

    public static object[] BindArguments(
        ToolDescriptor tool,
        IReadOnlyDictionary<string, string?> optionValues)
    {
        var knownOptions = tool.Parameters
            .Select(parameter => parameter.OptionName)
            .ToHashSet(StringComparer.Ordinal);
        var unknownOption = optionValues.Keys.FirstOrDefault(option => !knownOptions.Contains(option));
        if (unknownOption != null)
        {
            throw new InvalidOperationException($"未知选项 '--{unknownOption}'。tool: {tool.Name}");
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
        ToolDescriptor tool,
        object toolInstance,
        object[] arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var argumentIndex = 0;
        var invocationArguments = tool.Method.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken) ? (object)cancellationToken : arguments[argumentIndex++]).ToArray();
        object? result;
        try { result = tool.Method.Invoke(toolInstance, invocationArguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        return result switch
        {
            string text => text,
            Task<string> task => await task,
            Task<CallToolResult> task => GetPayload(await task),
            _ => throw new InvalidOperationException($"tool '{tool.Name}' 返回类型不受支持。")
        };
    }

    private static string GetPayload(CallToolResult result) => result.Content is [TextContentBlock text]
        ? text.Text : throw new InvalidOperationException("工具未返回单个 JSON 文本结果。");

    public static bool IsToolFailure(string payload) => ToolResultStatus.IsFailure(payload);

    public static bool IsToolFailure(JsonElement result) =>
        ToolResultStatus.IsFailure(result);

    public static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null }
            ? exception.InnerException
            : exception;
    }

    private static object ConvertValue(ToolParameterDescriptor parameter, string? rawValue)
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

        if (targetType == typeof(Dictionary<string, JsonElement>))
        {
            try
            {
                using var document = JsonDocument.Parse(rawValue ?? "null");
                MongoDocumentContract.ValidateObject(document.RootElement);
                return document.RootElement.Deserialize<Dictionary<string, JsonElement>>()!;
            }
            catch (JsonException) { throw new InvalidOperationException("参数需要 JSON / Extended JSON v2 对象。"); }
            catch (DatabaseMcpServer.Models.DatabaseMcpException ex) { throw new InvalidOperationException(ex.Message); }
        }

        if (targetType.IsArray)
        {
            try
            {
                return JsonSerializer.Deserialize(rawValue ?? "null", targetType,
                    ArgumentJson)!;
            }
            catch (JsonException ex) { throw new InvalidOperationException("参数需要类型化 JSON 数组。", ex); }
        }

        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 使用了不支持的参数类型 '{targetType.Name}'。");
    }
}
