using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

public record DatabaseOperationContext
{
    public string DatabaseName { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    [JsonIgnore] public string ConnectionString { get; init; } = string.Empty;
    public string ConnectionIdentity { get; init; } = string.Empty;
    public long ConfigurationVersion { get; init; }
    public bool EnableDangerousOperations { get; init; }
    public string? AccessMode { get; init; }
    public IReadOnlyDictionary<string, string>? OptimizationSettings { get; init; }

    protected DatabaseOperationContext() { }

    internal DatabaseOperationContext(DatabaseConnection connection, string identity)
    {
        DatabaseName = connection.Name;
        DatabaseType = connection.DbType;
        ConnectionString = connection.ConnectionString;
        ConnectionIdentity = identity;
        ConfigurationVersion = connection.ConfigurationVersion;
        EnableDangerousOperations = connection.EnableDangerousOperations;
        AccessMode = connection.AccessMode;
        OptimizationSettings = connection.OptimizationSettings == null ? null
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(connection.OptimizationSettings));
    }

    internal DatabaseConnection ToConnection() => new()
    {
        Name = DatabaseName, DbType = DatabaseType, ConnectionString = ConnectionString,
        ConfigurationVersion = ConfigurationVersion, EnableDangerousOperations = EnableDangerousOperations,
        AccessMode = AccessMode, OptimizationSettings = OptimizationSettings?.ToDictionary(p => p.Key, p => p.Value)
    };

    public override string ToString() => $"{DatabaseName} ({DatabaseType}, version {ConfigurationVersion})";
}
