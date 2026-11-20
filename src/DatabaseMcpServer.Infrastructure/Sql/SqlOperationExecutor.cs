using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Diagnostics;

namespace DatabaseMcpServer.Services;

internal abstract class SqlOperationExecutor : DatabaseOperationExecutor
{
    private readonly DatabaseMetadataCache _metadataCache;

    protected SqlOperationExecutor(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper,
        ILogger logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions)
        : base(databaseConfig, logger, executionOptions)
    {
        Sessions = sessions;
        DatabaseHelper = databaseHelper;
        _metadataCache = metadataCache;
    }

    protected SqlDatabaseSessions Sessions { get; }
    protected IDatabaseHelperService DatabaseHelper { get; }
    protected virtual DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Read;

    private static void EnsureProvider(DatabaseClientContext context)
        => DatabaseProviderCatalog.EnsureSqlTools(context.Client.CurrentConnectionConfig.DbType);

    protected object WithClient(Func<ISqlSugarClient, object> action, string? databaseName = null,
        DatabaseToolAccess? requiredAccess = null)
    {
        return Execute(() =>
        {
            using var lease = databaseName == null ? Sessions.AcquireClient() : Sessions.AcquireClient(databaseName);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, requiredAccess ?? RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            object result;
            try { result = SqlCommandTimeout.WithTimeout(lease.Client, ExecutionOptions.ResolveTimeout(null), action); }
            finally
            {
                if ((requiredAccess ?? RequiredAccess) > DatabaseToolAccess.Read) _metadataCache.Invalidate(lease.Context);
            }
            return result;
        });
    }

    protected object WithClientContext(Func<DatabaseClientContext, object> action, string? databaseName = null,
        DatabaseToolAccess? requiredAccess = null)
    {
        return Execute(() =>
        {
            using var lease = databaseName == null ? Sessions.AcquireClient() : Sessions.AcquireClient(databaseName);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, requiredAccess ?? RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            object result;
            try
            {
                result = SqlCommandTimeout.WithTimeout(lease.Client, ExecutionOptions.ResolveTimeout(null), _ => action(lease.Context));
            }
            finally { if ((requiredAccess ?? RequiredAccess) > DatabaseToolAccess.Read) _metadataCache.Invalidate(lease.Context); }
            return result;
        });
    }

    protected Task<object> WithClientAsync(Func<ISqlSugarClient, Task<object>> action,
        CancellationToken cancellationToken = default, string? databaseName = null)
        => WithClientContextAsync(context => action(context.Client), cancellationToken, databaseName);

    protected async Task<object> WithClientContextAsync(Func<DatabaseClientContext, Task<object>> action,
        CancellationToken cancellationToken = default, string? databaseName = null)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = await (databaseName == null ? Sessions.AcquireClientAsync(cancellationToken)
                : Sessions.AcquireClientAsync(databaseName, cancellationToken)).ConfigureAwait(false);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            var context = lease.Context;
            try
            {
                var result = await action(context).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            finally { if (RequiredAccess > DatabaseToolAccess.Read) _metadataCache.Invalidate(context); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { DatabaseTelemetry.Cancelled.Add(1); throw; }
        catch (Exception ex)
        {
            return DatabaseErrorMapper.Map(ex, Logger);
        }
        finally { DatabaseTelemetry.RequestMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
}
