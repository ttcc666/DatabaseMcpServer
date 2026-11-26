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
                return ValidateChoices(parameter, ConvertJsonValue(parameter, value));
            if (parameter.IsRequired)
                throw new InvalidOperationException($"缺少必填选项 '--{parameter.OptionName}'。");
            return parameter.DefaultValue!;
        }).ToArray();
    }

    private static object ConvertJsonValue(ToolParameterDescriptor parameter, JsonElement value)
    {
        var type = parameter.EffectiveType;
        if (type == typeof(JsonElement)) return value.Clone();
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (parameter.AllowsNull) return null!;
            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 不接受 null。");
        }
        if (type == typeof(string))
        {
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
            try { ValidateJsonArray(type, value); return value.Deserialize(type, ArgumentJson)!; }
            catch (JsonException ex) { throw new InvalidOperationException("参数需要类型化 JSON 数组。", ex); }
        }
        if (type == typeof(DatabaseMcpServer.Models.ColumnDefinition))
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("column 需要 JSON 对象。");
            ValidateUniqueProperties(value);
            try { return value.Deserialize(type, ArgumentJson)!; }
            catch (JsonException ex) { throw new InvalidOperationException("column 字段契约无效。", ex); }
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
                values[i] = ValidateChoices(parameter, ConvertValue(parameter, rawValue));
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
        using var activity = DatabaseTelemetry.StartTool();
        cancellationToken.ThrowIfCancellationRequested();
        var argumentIndex = 0;
        var invocationArguments = tool.Method.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken)
                ? (object?)cancellationToken
                : parameter.ParameterType == typeof(IProgress<ModelContextProtocol.ProgressNotificationValue>)
                    ? null
                    : arguments[argumentIndex++]).ToArray();
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
            CallToolResult structured => GetPayload(structured),
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

        if (targetType == typeof(JsonElement))
        {
            try { using var document = JsonDocument.Parse(rawValue ?? "null"); return document.RootElement.Clone(); }
            catch (JsonException ex) { throw new InvalidOperationException("参数需要有效 JSON。", ex); }
        }

        if (targetType.IsArray || targetType == typeof(DatabaseMcpServer.Models.ColumnDefinition))
        {
            try
            {
                using var json = JsonDocument.Parse(rawValue ?? "null");
                return ConvertJsonValue(parameter, json.RootElement);
            }
            catch (JsonException ex) { throw new InvalidOperationException("参数需要类型化 JSON 数组。", ex); }
        }

        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 使用了不支持的参数类型 '{targetType.Name}'。");
    }

    private static object ValidateChoices(ToolParameterDescriptor parameter, object value)
    {
        if (parameter.Choices == null || value == null) return value!;
        var values = value is string text ? new[] { text } : (string[])value;
        if (values.Any(item => !parameter.Choices.Contains(item, StringComparer.Ordinal)))
            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 仅接受: {string.Join(", ", parameter.Choices)}。");
        return value;
    }

    private static void ValidateJsonArray(Type type, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("参数需要 JSON 数组。");
        if (type.GetElementType() != typeof(Dictionary<string, JsonElement>))
        {
            ValidateUniqueProperties(value);
            return;
        }
        try
        {
            if (value.ValueKind != JsonValueKind.Array) throw MongoDocumentContract.Invalid("参数需要 JSON 对象数组。");
            foreach (var item in value.EnumerateArray()) MongoDocumentContract.ValidateObject(item);
        }
        catch (DatabaseMcpServer.Models.DatabaseMcpException ex) { throw new InvalidOperationException(ex.Message); }
    }

    private static void ValidateUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidOperationException("类型化 JSON 参数不允许重复属性。");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateUniqueProperties(item);
    }
}
