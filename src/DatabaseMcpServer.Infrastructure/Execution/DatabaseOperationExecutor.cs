using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Services;

internal abstract class DatabaseOperationExecutor(
    IDatabaseConfigService databaseConfig, ILogger logger, DatabaseExecutionOptions executionOptions)
{
    protected IDatabaseConfigService DatabaseConfig { get; } = databaseConfig;
    protected ILogger Logger { get; } = logger;
    protected DatabaseExecutionOptions ExecutionOptions { get; } = executionOptions;

    protected object Execute(Func<object> action)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return DatabaseErrorMapper.Map(ex, Logger);
        }
    }

    protected async Task<object> ExecuteAsync(Func<Task<object>> action, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return DatabaseErrorMapper.Map(ex, Logger);
        }
    }

}
