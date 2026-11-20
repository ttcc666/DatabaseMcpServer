using SqlSugar;

namespace DatabaseMcpServer.Models;

/// <summary>
/// 将数据库客户端与创建它时的连接级执行策略绑定为同一快照。
/// </summary>
public sealed record DatabaseClientContext(
    ISqlSugarClient Client,
    bool EnableDangerousOperations,
    string ConnectionIdentity)
{
    public string DatabaseName { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public long ConfigurationVersion { get; init; }
    public string? AccessMode { get; init; }
    public IReadOnlyDictionary<string, string>? OptimizationSettings { get; init; }
}
