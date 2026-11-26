using DatabaseMcpServer.Tools;
using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebToolService
{
    private readonly ToolCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _invocationGate = new(1, 1);

    public CliWebToolService(ToolCatalog catalog, IServiceProvider serviceProvider)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
    }

    public object GetTools()
    {
        var selected = ToolExposurePolicy.SelectFor(_serviceProvider, _catalog);
        return new
        {
            success = true,
            tools = _catalog.Tools.Where(tool => selected.Contains(tool.Name)).Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                category = tool.Category,
                provider = tool.Provider,
                supportedProviders = tool.SupportedProviders,
                unsupportedProviders = tool.UnsupportedProviders,
                requiredAccess = tool.RequiredAccessName,
                inputSchema = tool.ProtocolTool.InputSchema,
                outputSchema = tool.ProtocolTool.OutputSchema,
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
        if (!ToolExposurePolicy.SelectFor(_serviceProvider, _catalog).Contains(toolName))
            throw new InvalidOperationException($"tool '{toolName}' 未在当前配置和权限下启用。");

        if (tool.RequiresConfirmation && !string.Equals(request.Confirmation, tool.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"tool '{tool.Name}' 需要输入完整名称确认。");
        }

        var arguments = ToolInvoker.BindJsonArguments(tool, request.Arguments ?? new Dictionary<string, JsonElement>());

        var queuedAt = Stopwatch.GetTimestamp();
        var serialize = tool.RequiresConfirmation || tool.Category == "connection";
        if (serialize) await _invocationGate.WaitAsync(cancellationToken);
        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var instance = _serviceProvider.GetRequiredService(tool.ToolType);
            var payload = await ToolInvoker.InvokeAsync(tool, instance, arguments, cancellationToken);
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
            throw new InvalidOperationException(ToolInvoker.Unwrap(ex).Message, ex);
        }
        finally
        {
            if (serialize) _invocationGate.Release();
        }
    }

    private static (object Result, bool ToolSuccess) ParsePayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return (document.RootElement.Clone(), !ToolInvoker.IsToolFailure(document.RootElement));
        }
        catch (JsonException)
        {
            return (payload, true);
        }
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
