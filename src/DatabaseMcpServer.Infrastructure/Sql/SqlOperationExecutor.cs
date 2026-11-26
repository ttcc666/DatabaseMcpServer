using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

internal abstract class SqlOperationExecutor : DatabaseOperationExecutor
{
    private readonly DatabaseMetadataCache _metadataCache;
    private readonly SqlRequestExecutor _requests;

    protected SqlOperationExecutor(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper,
        ILogger logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions)
        : base(databaseConfig, logger, executionOptions)
    {
        Sessions = sessions;
        DatabaseHelper = databaseHelper;
        _metadataCache = metadataCache;
        _requests = new(sessions, executionOptions, metadataCache);
    }

    protected SqlDatabaseSessions Sessions { get; }
    protected IDatabaseHelperService DatabaseHelper { get; }
    protected virtual DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Read;

    private static void EnsureProvider(DatabaseClientContext context)
        => DatabaseProviderCatalog.EnsureSqlTools(context.Client.CurrentConnectionConfig.DbType);

    protected object WithClient(Func<ISqlSugarClient, object> action, string? databaseName = null,
        DatabaseToolAccess? requiredAccess = null)
        => WithClientContext(context => action(context.Client), databaseName, requiredAccess);

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

    protected Task<object> WithDeadlineAsync(Func<DatabaseClientContext, CancellationToken, Task<object>> action,
        int? timeoutSeconds, CancellationToken cancellationToken, string? databaseName = null)
        => _requests.ExecuteAsync(action, RequiredAccess, timeoutSeconds, cancellationToken, databaseName, Logger, error => error);
}
