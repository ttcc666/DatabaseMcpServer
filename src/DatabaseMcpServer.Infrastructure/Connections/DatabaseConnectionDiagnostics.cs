using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

internal static class DatabaseConnectionDiagnostics
{
    public static async Task<(bool Connected, string Message)> CheckAsync(DatabaseExecutionCoordinator execution, DatabaseConnection connection,
        DatabaseConnectionProbe probe, CancellationToken cancellationToken = default)
    {
        // Diagnostics validate the saved candidate, which may differ from the active runtime version.
        // The factory isolates an unpublished version while sharing its admission limit.
        try
        {
            using var reservation = execution.Reserve(connection);
            using var lease = await reservation.AcquireAsync(cancellationToken);
            await probe.CheckAsync(lease.Context, cancellationToken);
            return (true, $"数据库 '{connection.Name}' 连接成功");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { return (false, ex.Message); }
    }
}
