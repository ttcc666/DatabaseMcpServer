using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using DatabaseMcpServer.Helpers;
using System.Diagnostics;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

internal abstract class McpToolBase
{
    private readonly IJsonResultSerializer _resultSerializer;
    private readonly DatabaseMetadataCache _metadataCache;

    protected McpToolBase(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache)
    {
        DatabaseConfig = databaseConfig;
        DatabaseHelper = databaseHelper;
        _resultSerializer = resultSerializer;
        Logger = logger;
        ExecutionOptions = executionOptions;
        _metadataCache = metadataCache;
    }

    protected IDatabaseConfigService DatabaseConfig { get; }

    protected IDatabaseHelperService DatabaseHelper { get; }

    protected ILogger Logger { get; }
    protected DatabaseExecutionOptions ExecutionOptions { get; }
    protected virtual DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Read;
    protected virtual bool RequiresSql => false;

    private void EnsureProvider(DatabaseClientContext context)
    {
        if (RequiresSql) DatabaseProviderCatalog.EnsureSqlTools(context.Client.CurrentConnectionConfig.DbType);
    }

    protected string Execute(Func<object> action)
    {
        try
        {
            return _resultSerializer.Serialize(action());
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, Logger);
        }
    }

    protected async Task<string> ExecuteAsync(Func<Task<object>> action, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            return _resultSerializer.Serialize(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, Logger);
        }
    }

    protected string ExecuteRaw(Func<string> action)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, Logger);
        }
    }

    protected string WithClient(Func<ISqlSugarClient, object> action, string? databaseName = null,
        DatabaseToolAccess? requiredAccess = null)
    {
        return ExecuteRaw(() =>
        {
            using var lease = databaseName == null ? DatabaseConfig.AcquireClient() : DatabaseConfig.AcquireClient(databaseName);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, requiredAccess ?? RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            object result;
            try { result = SqlCommandTimeout.WithTimeout(lease.Client, ExecutionOptions.ResolveTimeout(null), action); }
            finally
            {
                if ((requiredAccess ?? RequiredAccess) > DatabaseToolAccess.Read) _metadataCache.Invalidate(lease.Context);
            }
            return DatabaseTelemetry.Serialize(() => _resultSerializer.Serialize(result));
        });
    }

    protected string WithNamedClient(string databaseName, Func<ISqlSugarClient, object> action)
    {
        return ExecuteRaw(() =>
        {
            using var lease = DatabaseConfig.AcquireClient(databaseName);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            var result = SqlCommandTimeout.WithTimeout(lease.Client, ExecutionOptions.ResolveTimeout(null), action);
            return DatabaseTelemetry.Serialize(() => _resultSerializer.Serialize(result));
        });
    }

    protected string WithClientContext(Func<DatabaseClientContext, object> action, string? databaseName = null,
        DatabaseToolAccess? requiredAccess = null)
    {
        return ExecuteRaw(() =>
        {
            using var lease = databaseName == null ? DatabaseConfig.AcquireClient() : DatabaseConfig.AcquireClient(databaseName);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, requiredAccess ?? RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            object result;
            try { result = lease.Client.CurrentConnectionConfig.DbType == DbType.MongoDb ? action(lease.Context)
                : SqlCommandTimeout.WithTimeout(lease.Client, ExecutionOptions.ResolveTimeout(null), _ => action(lease.Context)); }
            finally { if ((requiredAccess ?? RequiredAccess) > DatabaseToolAccess.Read) _metadataCache.Invalidate(lease.Context); }
            return DatabaseTelemetry.Serialize(() => _resultSerializer.Serialize(result));
        });
    }

    protected Task<string> WithClientAsync(Func<ISqlSugarClient, Task<object>> action,
        CancellationToken cancellationToken = default, string? databaseName = null)
        => WithClientContextAsync(context => action(context.Client), cancellationToken, databaseName);

    protected async Task<string> WithClientContextAsync(Func<DatabaseClientContext, Task<object>> action,
        CancellationToken cancellationToken = default, string? databaseName = null)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = await (databaseName == null ? DatabaseConfig.AcquireClientAsync(cancellationToken)
                : DatabaseConfig.AcquireClientAsync(databaseName, cancellationToken)).ConfigureAwait(false);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, RequiredAccess, ExecutionOptions.Access);
            EnsureProvider(lease.Context);
            var context = lease.Context;
            try
            {
                var result = await action(context).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return DatabaseTelemetry.Serialize(() => _resultSerializer.Serialize(result));
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
            return McpExceptionFilter.HandleException(ex, Logger);
        }
        finally { DatabaseTelemetry.RequestMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
}
