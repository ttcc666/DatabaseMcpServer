using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

// Observe an acquired context without depending on reuse of a disposed lease.
internal sealed class ObservedClientFactory(ISqlSugarClientFactory inner, Action<ISqlSugarClient> observe)
    : ISqlSugarClientFactory, IDisposable
{
    public static void Register(IServiceCollection services, Action<ISqlSugarClient> observe)
    {
        services.RemoveAll<ISqlSugarClientFactory>();
        services.AddSingleton<ISqlSugarClientFactory>(sp => new ObservedClientFactory(
            new SqlSugarClientFactory(sp.GetRequiredService<ILogger<SqlSugarClientFactory>>(),
                sp.GetRequiredService<IDatabaseOptimizationStrategyFactory>(), sp.GetRequiredService<DatabaseExecutionOptions>()), observe));
    }

    public DatabaseClientLease CreateClient(DatabaseOperationLease operation) => Observe(inner.CreateClient(operation));

    public DatabaseClientLease AcquireClient(DatabaseConnection connection) => Observe(inner.AcquireClient(connection));

    public DatabaseClientReservation ReserveClient(DatabaseConnection connection)
    {
        var reservation = inner.ReserveClient(connection);
        return new(token => Observe(reservation.Acquire(token)),
            async token => Observe(await reservation.AcquireAsync(token)), reservation.Dispose);
    }

    private DatabaseClientLease Observe(DatabaseClientLease lease)
    {
        try { observe(lease.Client); return lease; }
        catch { lease.Dispose(); throw; }
    }

    public void UpdateConnections(IReadOnlyDictionary<string, DatabaseConnection> connections, string? previousName = null, string? newName = null)
        => inner.UpdateConnections(connections, previousName, newName);
    public void Dispose() => (inner as IDisposable)?.Dispose();
}
