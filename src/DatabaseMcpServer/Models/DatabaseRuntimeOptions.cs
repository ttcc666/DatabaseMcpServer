namespace DatabaseMcpServer.Models;

internal sealed record DatabaseRuntimeOptions(bool? EnableMonitorConfig, string? ConfigPath = null)
{
    public static DatabaseRuntimeOptions Default { get; } = new((bool?)null);
}
