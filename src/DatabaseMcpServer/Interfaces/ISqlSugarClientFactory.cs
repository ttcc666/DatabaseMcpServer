using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Interfaces;

public interface ISqlSugarClientFactory
{
    DatabaseClientReservation ReserveClient(DatabaseConnection connection);
    DatabaseClientLease AcquireClient(DatabaseConnection connection);
    void UpdateConnections(IReadOnlyDictionary<string, DatabaseConnection> connections, string? previousName = null, string? newName = null);
}
