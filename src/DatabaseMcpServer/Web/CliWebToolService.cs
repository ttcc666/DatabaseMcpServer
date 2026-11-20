using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebToolService
{
    private readonly CliToolCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _invocationGate = new(1, 1);

    public CliWebToolService(CliToolCatalog catalog, IServiceProvider serviceProvider)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
    }

    public object GetTools()
    {
        return new
        {
            success = true,
            tools = _catalog.Tools.Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                category = GetCategory(tool.ToolType),
                requiresConfirmation = tool.RequiresConfirmation,
                parameters = tool.Parameters.Select(parameter => new
                {
                    name = parameter.ParameterName,
                    optionName = parameter.OptionName,
                    description = parameter.Description,
                    type = parameter.DisplayTypeName,
                    required = parameter.IsRequired,
                    defaultValue = FormatDefaultValue(parameter.DefaultValue)
                }).ToArray()
            }).ToArray()
        };
    }

    public async Task<object> InvokeAsync(
        string toolName,
        CliWebToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_catalog.TryGetTool(toolName, out var tool))
        {
            throw new KeyNotFoundException($"未知 tool '{toolName}'。");
        }

        if (tool.RequiresConfirmation && !string.Equals(request.Confirmation, tool.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"tool '{tool.Name}' 需要输入完整名称确认。");
        }

        var values = ConvertArguments(tool, request.Arguments ?? new Dictionary<string, JsonElement>());
        var arguments = CliToolInvoker.BindArguments(tool, values);

        var queuedAt = Stopwatch.GetTimestamp();
        var serialize = tool.RequiresConfirmation || tool.ToolType == typeof(DatabaseMcpServer.Tools.Management.ConnectionTools);
        if (serialize) await _invocationGate.WaitAsync(cancellationToken);
        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var instance = _serviceProvider.GetRequiredService(tool.ToolType);
            var payload = await CliToolInvoker.InvokeAsync(tool, instance, arguments, cancellationToken);
            var finishedAt = Stopwatch.GetTimestamp();
            var (result, toolSuccess) = ParsePayload(payload);

            return new
            {
                success = true,
                toolName = tool.Name,
                durationMs = (long)Stopwatch.GetElapsedTime(startedAt, finishedAt).TotalMilliseconds,
                queueMs = (long)Stopwatch.GetElapsedTime(queuedAt, startedAt).TotalMilliseconds,
                toolSuccess,
                result
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(CliToolInvoker.Unwrap(ex).Message, ex);
        }
        finally
        {
            if (serialize) _invocationGate.Release();
        }
    }

    private static Dictionary<string, string?> ConvertArguments(
        CliToolMetadata tool,
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var parameters = tool.Parameters.ToDictionary(item => item.OptionName, StringComparer.Ordinal);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, element) in arguments)
        {
            if (!parameters.TryGetValue(name, out var parameter))
            {
                throw new InvalidOperationException($"未知选项 '--{name}'。tool: {tool.Name}");
            }

            values[name] = ConvertArgument(parameter, element);
        }

        return values;
    }

    private static string? ConvertArgument(CliToolParameterMetadata parameter, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (parameter.IsJson)
        {
            return value.GetRawText();
        }

        if (parameter.EffectiveType == typeof(string))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要字符串值。");
            }

            return value.GetString();
        }

        if (parameter.EffectiveType == typeof(int) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (parameter.EffectiveType == typeof(bool) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False))
        {
            return value.GetBoolean() ? "true" : "false";
        }

        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 的 JSON 类型不正确。");
    }

    private static (object Result, bool ToolSuccess) ParsePayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return (document.RootElement.Clone(), !CliToolInvoker.IsToolFailure(document.RootElement));
        }
        catch (JsonException)
        {
            return (payload, true);
        }
    }

    private static string GetCategory(Type toolType)
    {
        var name = toolType.Name;
        return name switch
        {
            "ConnectionTools" => "connection",
            "SchemaTools" => "schema",
            "QueryTools" => "query",
            "DatabaseQueryTools" => "query",
            "CommandTools" => "command",
            _ => "other"
        };
    }

    private static object? FormatDefaultValue(object? value)
    {
        return value switch
        {
            JsonElement element => element.Clone(),
            _ => value
        };
    }
}
