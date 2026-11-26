using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

internal sealed record DatabaseConfigurationSnapshot(
    string ConfigPath,
    Dictionary<string, DatabaseConnection> Connections,
    string? DefaultDatabaseName,
    bool EnableMonitorConfig,
    string? ContentHash = null)
{
    public static DatabaseConfigurationSnapshot Empty { get; } =
        new(string.Empty, new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal), null, false);
}
