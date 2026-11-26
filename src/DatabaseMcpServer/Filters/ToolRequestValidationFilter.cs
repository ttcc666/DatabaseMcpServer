using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Filters;

internal sealed class ToolRequestValidationFilter
{
    public async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken, Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> next)
    {
        using var activity = DatabaseTelemetry.StartTool();
        var services = context.Services ?? context.Server.Services!;
        var policy = services.GetRequiredService<ToolExposurePolicy>();
        var catalog = services.GetRequiredService<ToolCatalog>();
        var configuration = services.GetRequiredService<IDatabaseConfigService>();
        var options = services.GetRequiredService<DatabaseExecutionOptions>();
        policy.Observe(context.Server, catalog, configuration, options);
        if (context.Params is { } request && catalog.TryGetTool(request.Name, out var tool))
        {
            if (!policy.Select(catalog, configuration, options).Contains(request.Name))
                return McpResultAdapter.Structured(ApiResult<object>.CreateError("当前 MCP 工具集未启用此工具。", DatabaseErrorCode.AccessDenied), false);
            try
            {
                var parameters = tool.Parameters.ToDictionary(p => p.ParameterName, p => p.OptionName, StringComparer.Ordinal);
                var arguments = request.Arguments ?? new Dictionary<string, System.Text.Json.JsonElement>();
                if (arguments.Keys.Any(name => !parameters.ContainsKey(name)))
                    throw new InvalidOperationException("包含未知参数，请按当前 tools/list schema 调用。");
                // Share JSON shape, nullability and duplicate-key rules with CLI/Web.
                ToolInvoker.BindJsonArguments(tool, arguments.ToDictionary(p => parameters[p.Key], p => p.Value));
            }
            catch (InvalidOperationException ex)
            {
                if (tool.IsCollection)
                    return McpResultAdapter.Structured(new MongoCollectionResult { Success = false, ErrorCode = 1004,
                        ErrorMessage = ex.Message, Retryable = false, Phase = "validation", WriteOutcome = tool.RequiresConfirmation ? "not-executed" : null }, false);
                return McpResultAdapter.Structured(ApiResult<object>.CreateError(ex.Message, DatabaseErrorCode.InvalidParameters), false);
            }
        }
        var result = await next(context, cancellationToken);
        if (result.StructuredContent is { } structured ? ToolResultStatus.IsFailure(structured)
            : result.Content is [TextContentBlock text] && ToolResultStatus.IsFailure(text.Text))
            result.IsError = true;
        return result;
    }
}
