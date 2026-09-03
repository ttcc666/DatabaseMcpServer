namespace DatabaseMcpServer.Models;

internal sealed record DatabaseRuntimeOptions(bool? EnableMonitorConfig)
{
    public static DatabaseRuntimeOptions Default { get; } = new((bool?)null);
}
