using System.Text.Json;

namespace DatabaseMcpServer.Models;

public sealed class ExplainQueryResult : ExplainQueryResult<Dictionary<string, JsonElement>>;

public class ExplainQueryResult<TRow>
{
    public bool Success { get; init; }
    public string? DatabaseName { get; init; }
    public string? Provider { get; init; }
    public string? PlanFormat { get; init; }
    public string? ParameterMode { get; init; }
    public bool Estimated { get; init; } = true;
    public bool Executed { get; init; }
    public List<TRow>? Plan { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool? Retryable { get; init; }
    public string? Phase { get; init; }
    public string? Capability { get; init; }
    public string? Hint { get; init; }
}
