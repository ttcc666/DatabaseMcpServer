using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DatabaseMcpServer.Services;

internal sealed class SqlRequestExecutor(SqlDatabaseSessions sessions, DatabaseExecutionOptions options, DatabaseMetadataCache cache)
{
    public async Task<T> ExecuteAsync<T>(Func<DatabaseClientContext, CancellationToken, Task<T>> action,
        DatabaseToolAccess access, int? timeoutSeconds, CancellationToken cancellationToken, string? databaseName,
        ILogger logger, Func<ApiResult<object>, T> failure)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancellation = DatabaseTelemetry.MeasureCancellation(deadline.Token);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timeout = options.ResolveTimeout(timeoutSeconds ?? Math.Min(options.DefaultCommandTimeoutSeconds, options.MaxCommandTimeoutSeconds));
            var started = Stopwatch.GetTimestamp();
            if (timeout > 0) deadline.CancelAfter(TimeSpan.FromSeconds(timeout.Value));
            using var lease = await (databaseName == null ? sessions.AcquireClientAsync(deadline.Token)
                : sessions.AcquireClientAsync(databaseName, deadline.Token)).ConfigureAwait(false);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, access, options.Access);
            DatabaseProviderCatalog.EnsureSqlTools(lease.Client.CurrentConnectionConfig.DbType);
            try
            {
                // Native timers can fire early (MySqlConnector coalesces timers within 15 ms).
                // The request token owns the deadline; keep the driver timer as a later fallback.
                var driverTimeout = timeout is > 0 ? timeout + 1 : timeout;
                var result = await SqlCommandTimeout.WithTimeoutAsync(lease.Client, driverTimeout, deadline.Token,
                    _ => action(lease.Context, deadline.Token)).ConfigureAwait(false);
                if (timeout > 0 && Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(timeout.Value))
                    deadline.Cancel();
                deadline.Token.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                if (access > DatabaseToolAccess.Read) cache.Invalidate(lease.Context);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            DatabaseTelemetry.Cancelled.Add(1);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            DatabaseTelemetry.Cancelled.Add(1);
            DatabaseTelemetry.RequestTimeouts.Add(1);
            return failure(DatabaseErrorMapper.Map(new DatabaseMcpException(DatabaseErrorCode.CommandTimeout,
                "请求总超时（包含排队、执行和读取）。"), logger));
        }
        catch (Exception ex)
        {
            return failure(DatabaseErrorMapper.Map(ex, logger));
        }
    }
}
