using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Tools;

internal abstract class McpToolBase
{
    private readonly IJsonResultSerializer _resultSerializer;

    protected McpToolBase(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger logger)
    {
        DatabaseConfig = databaseConfig;
        DatabaseHelper = databaseHelper;
        _resultSerializer = resultSerializer;
        Logger = logger;
    }

    protected IDatabaseConfigService DatabaseConfig { get; }

    protected IDatabaseHelperService DatabaseHelper { get; }

    protected ILogger Logger { get; }

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

    protected async Task<string> ExecuteAsync(Func<Task<object>> action)
    {
        try
        {
            var result = await action();
            return _resultSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, Logger);
        }
    }

    protected string ExecuteRaw(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, Logger);
        }
    }

    protected string WithClient(Func<ISqlSugarClient, object> action)
    {
        return Execute(() =>
        {
            var db = DatabaseConfig.CreateClient();
            return action(db);
        });
    }

    protected string WithNamedClient(string databaseName, Func<ISqlSugarClient, object> action)
    {
        return Execute(() =>
        {
            var db = DatabaseConfig.CreateClient(databaseName);
            return action(db);
        });
    }

    protected string WithClientContext(Func<DatabaseClientContext, object> action)
    {
        return Execute(() => action(DatabaseConfig.CreateClientContext()));
    }

    protected Task<string> WithClientAsync(Func<ISqlSugarClient, Task<object>> action)
    {
        return ExecuteAsync(async () =>
        {
            var db = DatabaseConfig.CreateClient();
            return await action(db);
        });
    }
}
