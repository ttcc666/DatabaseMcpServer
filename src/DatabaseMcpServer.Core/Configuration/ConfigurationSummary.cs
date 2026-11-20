namespace DatabaseMcpServer.Models;

public sealed record ConfigurationSummary
{
    public bool Configured { get; init; }
    public string Mode { get; init; } = string.Empty;
    public int TotalDatabases { get; init; }
    public string CurrentDatabase { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public bool EnableDangerousOperations { get; init; }
    public bool EnableMonitorConfigEnabled { get; init; }
    public bool EnableMonitorConfig { get; init; }
    public string Message { get; init; } = string.Empty;
}
