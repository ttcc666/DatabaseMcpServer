namespace DatabaseMcpServer.Models;

public sealed record DatabaseConnectionUpdate
{
    public string? NewName { get; init; }
    public string? DbType { get; init; }
    public string? ConnectionString { get; init; }
    public string? Description { get; init; }
    public bool ClearDescription { get; init; }
    public bool SetDefault { get; init; }
    public bool EnableDangerousOperations { get; init; }
    public bool ApplyDbType { get; init; }
    public bool ApplyConnectionString { get; init; }
    public bool ApplyDescription { get; init; }
    public bool ApplyClearDescription { get; init; }
    public bool ApplySetDefault { get; init; }
    public bool ApplyEnableDangerousOperations { get; init; }
    internal Func<string, string?>? BuildConnectionString { get; init; }
}
