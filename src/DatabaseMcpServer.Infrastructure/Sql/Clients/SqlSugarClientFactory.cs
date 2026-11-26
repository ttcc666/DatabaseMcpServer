using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Diagnostics;

namespace DatabaseMcpServer.Services;

internal sealed class SqlSugarClientFactory : ISqlSugarClientFactory, IDisposable
{
    private readonly ILogger<SqlSugarClientFactory> _logger;
    private readonly SqlSugarConnectionSettings _settings;
    private readonly DatabaseExecutionCoordinator _execution;
    private readonly bool _ownsExecution;

    public SqlSugarClientFactory(ILogger<SqlSugarClientFactory> logger,
        IDatabaseOptimizationStrategyFactory strategyFactory, DatabaseExecutionOptions options,
        DatabaseExecutionCoordinator? execution = null)
    {
        _logger = logger; _settings = new(strategyFactory);
        options.Validate();
        _ownsExecution = execution == null;
        _execution = execution ?? new(options);
    }

    public DatabaseClientLease AcquireClient(DatabaseConnection connection)
    {
        using var reservation = ReserveClient(connection);
        return reservation.Acquire();
    }

    public DatabaseClientReservation ReserveClient(DatabaseConnection connection)
    {
        var reservation = _execution.Reserve(connection);
        return new(token => CreateClient(reservation.Acquire(token)),
            async token => CreateClient(await reservation.AcquireAsync(token)), reservation.Dispose);
    }

    public DatabaseClientLease CreateClient(DatabaseOperationLease operation)
    {
        SqlSugarClient? client = null;
        try
        {
            SqlSugarProviderWarmup.Warmup(null);
            client = new SqlSugarClient(_settings.Create(operation.Context.ToConnection()));
            ConfigureAop(client);
            var owned = client;
            return new(new DatabaseClientContext(client, operation.Context), () =>
            {
                try { DisposeClient(owned); } finally { operation.Dispose(); }
            });
        }
        catch
        {
            if (client != null) DisposeClient(client);
            operation.Dispose();
            throw;
        }
    }

    public void UpdateConnections(IReadOnlyDictionary<string, DatabaseConnection> connections, string? previousName = null, string? newName = null)
        => _execution.UpdateConnections(connections, previousName, newName);

    public void Dispose() { if (_ownsExecution) _execution.Dispose(); }

    private void DisposeClient(ISqlSugarClient client)
    {
        try { client.Dispose(); }
        catch (Exception ex)
        {
            if (client.CurrentConnectionConfig.DbType == DbType.MongoDb)
                _logger.LogWarning("释放 MongoDB 客户端失败: {Message}", MongoError.Sanitize(ex).Message);
            else
                _logger.LogWarning(ex, "释放失效数据库客户端失败");
        }
    }

    private void ConfigureAop(SqlSugarClient db)
    {
        db.Aop.OnLogExecuted = (_, _) => DatabaseTelemetry.SqlMs.Record(db.Ado.SqlExecutionTime.TotalMilliseconds);
        if (db.CurrentConnectionConfig.DbType == DbType.MongoDb)
        {
            // Mongo commands embed document values, so SQL text/exception logging is unsafe.
            db.Aop.OnLogExecuting = (_, _) => _logger.LogInformation("执行 MongoDB ADO 命令");
            db.Aop.OnError = error => _logger.LogError("MongoDB ADO 命令失败: {Message}", MongoError.Sanitize(error).Message);
            return;
        }
        db.Aop.OnLogExecuting = (sql, pars) =>
        {
            if (!_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(sql);
            var safeParameters = SqlLogSanitizer.FormatParametersForLog(pars);
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", safeSql, safeParameters);
        };

        db.Aop.OnError = exp =>
        {
            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(exp.Sql);
            _logger.LogError("SQL执行错误: {Message} | SQL: {Sql}", exp.Message, safeSql);
        };
    }
}
