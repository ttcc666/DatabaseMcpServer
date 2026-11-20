using SqlSugar;

namespace DatabaseMcpServer.Models;

public sealed record DatabaseClientContext : DatabaseOperationContext
{
    public ISqlSugarClient Client { get; }
    internal DatabaseClientContext(ISqlSugarClient client, DatabaseOperationContext context) : base(context) => Client = client;

    public DatabaseClientContext(ISqlSugarClient client, bool enableDangerousOperations, string connectionIdentity)
    {
        Client = client;
        ConnectionString = client.CurrentConnectionConfig.ConnectionString;
        EnableDangerousOperations = enableDangerousOperations;
        ConnectionIdentity = connectionIdentity;
    }
}
