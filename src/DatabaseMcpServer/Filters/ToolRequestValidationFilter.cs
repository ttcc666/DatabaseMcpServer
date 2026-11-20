using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Filters;

internal sealed class ToolRequestValidationFilter
{
    private readonly Dictionary<string, ToolDescriptor> _toolMetadata = new ToolCatalog().Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _argumentNames;

    public ToolRequestValidationFilter()
        => _argumentNames = _toolMetadata.Values.ToDictionary(tool => tool.Name,
            tool => tool.Parameters.Select(parameter => parameter.ParameterName).ToHashSet(StringComparer.Ordinal));

    public async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken, Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> next)
    {
        if (context.Params is { } request && _argumentNames.TryGetValue(request.Name, out var allowed))
        {
            var arguments = request.Arguments ?? new Dictionary<string, System.Text.Json.JsonElement>();
            var unknown = arguments.Keys.FirstOrDefault(name => !allowed.Contains(name));
            if (unknown != null)
            {
                if (_toolMetadata[request.Name].IsCollection)
                    return McpResultAdapter.Structured(new MongoCollectionResult() { Success = false, ErrorCode = 1004,
                        ErrorMessage = "包含未知参数，请按当前 tools/list schema 调用。",
                        WriteOutcome = _toolMetadata[request.Name].RequiresConfirmation ? "not-executed" : null }, false);
                return new CallToolResult { IsError = true, Content = [new TextContentBlock
                {
                    Text = System.Text.Json.JsonSerializer.Serialize(new { success = false, errorCode = 1004,
                        errorMessage = $"未知参数 '{unknown}'，请按当前 tools/list schema 调用。" })
                }] };
            }
            if (_toolMetadata[request.Name].IsCollection)
            {
                try
                {
                    foreach (var parameter in _toolMetadata[request.Name].Parameters)
                    {
                        if (!arguments.TryGetValue(parameter.ParameterName, out var value))
                        {
                            if (parameter.IsRequired) throw MongoDocumentContract.Invalid("缺少必填 Mongo 工具参数。");
                            continue;
                        }
                        if (parameter.IsJson) MongoDocumentContract.ValidateObject(value);
                        else if (value.ValueKind == System.Text.Json.JsonValueKind.Null && !parameter.IsRequired &&
                            (!parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null)) continue;
                        else if (parameter.EffectiveType == typeof(string) && value.ValueKind == System.Text.Json.JsonValueKind.String) continue;
                        else if (parameter.EffectiveType == typeof(bool) && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) continue;
                        else if (parameter.EffectiveType == typeof(int) && value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out _)) continue;
                        else throw MongoDocumentContract.Invalid("Mongo 工具参数类型不符合当前 tools/list schema。");
                    }
                }
                catch (DatabaseMcpException ex)
                {
                    return McpResultAdapter.Structured(new MongoCollectionResult() { Success = false, ErrorCode = (int)ex.ErrorCode,
                        ErrorMessage = ex.Message, WriteOutcome = _toolMetadata[request.Name].RequiresConfirmation ? "not-executed" : null }, false);
                }
            }
        }
        var result = await next(context, cancellationToken);
        var failed = result.StructuredContent is { } structured
            ? ToolResultStatus.IsFailure(structured)
            : result.Content is [TextContentBlock text] && ToolResultStatus.IsFailure(text.Text);
        if (failed)
        {
            result.IsError = true;
        }

        return result;
    }
}
