using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Interfaces;

public interface ISqlSugarClientFactory
{
    ISqlSugarClient CreateClient(DatabaseConnection connection);

    void ResetClientPool();
}
