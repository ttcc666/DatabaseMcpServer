using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库命令执行工具类，用于执行数据库的增删改操作、存储过程和事务。
/// </summary>
internal class SqlCommandOperations : SqlOperationExecutor
{
    public SqlCommandOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SqlCommandOperations> logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
    }

    protected override DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Write;

    public Task<object> ExecuteCommand(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
    {
        return WithClientContextAsync(async context =>
        {
            commandTimeoutSeconds = ExecutionOptions.ResolveTimeout(commandTimeoutSeconds);
            EnsureSafeSql(sql, context.EnableDangerousOperations);
            SqlAccessPolicy.EnsureCommandAllowed(context, sql, ExecutionOptions.Access);
            var parsedParams = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
            var affectedRows = await SqlCommandTimeout.WithTimeoutAsync(context.Client, commandTimeoutSeconds, cancellationToken, client =>
                client.Ado.ExecuteCommandAsync(sql, parsedParams));

            return new { success = true, affectedRows };
        }, cancellationToken, databaseName);
    }

    public object CallStoredProcedure(
        string procedureName,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        string? databaseName = null,
        int? maxResponseBytes = null)
    {
        return WithClientContext(context =>
        {
            var db = context.Client;
            var limits = ExecutionOptions.ResolveQuery(null, maxResponseBytes, commandTimeoutSeconds);
            commandTimeoutSeconds = limits.TimeoutSeconds;
            var sugarParams = SqlParameterBinder.Bind(parameters, DatabaseHelper.ParseDbType(context.DatabaseType), storedProcedure: true);

            using var result = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
                client.Ado.UseStoredProcedure().GetDataTable(procedureName, sugarParams));
            var rows = DatabaseHelper.ConvertDataTableToList(result);
            var outputValues = sugarParams
                .Where(p => p.Direction != System.Data.ParameterDirection.Input)
                .ToDictionary(p => p.ParameterName, p => p.Value == DBNull.Value ? null : p.Value);

            var response = new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                outputParameters = outputValues
            };
            return limits.Bytes is { } bytes ? BoundedQueryExecutor.EncodeResponse(response, bytes) : (object)response;
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object ExecuteCommandWithGo(
        string sql,
        int? commandTimeoutSeconds = null,
        string? databaseName = null)
    {
        return WithClientContext(context =>
        {
            commandTimeoutSeconds = ExecutionOptions.ResolveTimeout(commandTimeoutSeconds);
            EnsureSafeSql(sql, context.EnableDangerousOperations);
            DatabaseAccessPolicy.EnsureAllowed(context, DatabaseToolAccess.Schema, ExecutionOptions.Access);
            var affectedRows = SqlCommandTimeout.WithTimeout(context.Client, commandTimeoutSeconds, client =>
                client.Ado.ExecuteCommandWithGo(sql));
            return new
            {
                success = true,
                affectedRows
            };
        }, databaseName);
    }

    public Task<object> BatchExecuteCommands(
        DatabaseCommand[] commands,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
    {
        return WithClientContextAsync(async context =>
        {
            commandTimeoutSeconds = ExecutionOptions.ResolveTimeout(commandTimeoutSeconds);
            if (commands == null || commands.Length == 0 || commands.Any(command => command == null || string.IsNullOrWhiteSpace(command.Sql)))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 必须是包含非空 sql 的对象数组。");

            return await SqlCommandTimeout.WithTimeoutAsync(context.Client, commandTimeoutSeconds, cancellationToken, async client =>
            {
                var results = new List<object>();

                using (client.Ado.OpenAlways())
                {
                    for (var i = 0; i < commands.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var command = commands[i];
                            SqlAccessPolicy.EnsureCommandAllowed(context, command.Sql, ExecutionOptions.Access);
                            if (!context.EnableDangerousOperations && DatabaseHelper.DetectDangerousOperation(command.Sql))
                            {
                                results.Add(new { success = false, error = "检测到危险操作。请使用特定工具进行架构操作，或在当前连接配置中显式设置 enableDangerousOperations=true。", commandIndex = i });
                                continue;
                            }

                            var parameters = SqlParameterBinder.Bind(command.Parameters, client.CurrentConnectionConfig.DbType);
                            var affectedRows = await client.Ado.ExecuteCommandAsync(command.Sql, parameters);
                            results.Add(new { success = true, affectedRows, commandIndex = i });
                        }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
                        {
                            results.Add(new { success = false, error = ex.Message, commandIndex = i });
                        }
                    }
                }

                return new
                {
                    success = true,
                    totalCommands = commands.Length,
                    results
                };
            });
        }, cancellationToken, databaseName);
    }

    private void EnsureSafeSql(string sql, bool enableDangerousOperations)
    {
        if (!enableDangerousOperations && DatabaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到危险操作。请使用特定工具进行架构操作，或在当前连接配置中显式设置 enableDangerousOperations=true。");
        }
    }
}
