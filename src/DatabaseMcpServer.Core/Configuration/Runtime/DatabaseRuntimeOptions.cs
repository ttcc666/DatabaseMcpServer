namespace DatabaseMcpServer.Models;

internal sealed record DatabaseRuntimeOptions(bool? EnableMonitorConfig, string? ConfigPath = null)
{
    public bool AllowUnconfigured { get; init; }
    public static DatabaseRuntimeOptions Default { get; } = new((bool?)null);
}
