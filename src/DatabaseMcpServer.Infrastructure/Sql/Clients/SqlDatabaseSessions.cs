using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

internal sealed class SqlDatabaseSessions(IDatabaseConfigService databases, ISqlSugarClientFactory clients)
{
    public DatabaseClientLease AcquireClient()
    {
        using var reservation = ReserveClient(null);
        return reservation.Acquire();
    }

    public DatabaseClientLease AcquireClient(string databaseName)
    {
        using var reservation = ReserveClient(databaseName);
        return reservation.Acquire();
    }

    public async ValueTask<DatabaseClientLease> AcquireClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reservation = ReserveClient(null);
        return await reservation.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DatabaseClientLease> AcquireClientAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reservation = ReserveClient(databaseName);
        return await reservation.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    private DatabaseClientReservation ReserveClient(string? databaseName)
    {
        var reservation = databases.ReserveConnection(databaseName);
        return new(token => clients.CreateClient(reservation.Acquire(token)),
            async token => clients.CreateClient(await reservation.AcquireAsync(token)), reservation.Dispose);
    }

}
