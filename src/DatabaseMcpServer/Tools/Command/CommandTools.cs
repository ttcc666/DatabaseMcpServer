using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools.Command;

/// <summary>
/// 数据库命令执行工具类，用于执行数据库的增删改操作、存储过程和事务。
/// </summary>
[McpServerToolType]
internal class CommandTools : McpToolBase
{
    protected override bool RequiresSql => true;
    public CommandTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<CommandTools> logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache)
        : base(databaseConfig, databaseHelper, resultSerializer, logger, executionOptions, metadataCache)
    {
    }

    protected override DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Write;

    [McpServerTool(Destructive = true)]
    [Description("Execute INSERT/UPDATE/DELETE SQL after dangerous-operation detection, binding typed parameters, and return affectedRows.")]
    public Task<string> ExecuteCommand(
        [Description("SQL command to execute")] string sql,
        [Description("Typed input parameter array. NULL requires dbType.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
    {
        return WithClientContextAsync(async context =>
        {
            commandTimeoutSeconds = ExecutionOptions.ResolveTimeout(commandTimeoutSeconds);
            EnsureSafeSql(sql, context.EnableDangerousOperations);
            DatabaseAccessPolicy.EnsureCommandAllowed(context, sql, ExecutionOptions.Access);
            var parsedParams = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
            var affectedRows = await SqlCommandTimeout.WithTimeoutAsync(context.Client, commandTimeoutSeconds, cancellationToken, client =>
                client.Ado.ExecuteCommandAsync(sql, parsedParams));

            return new { success = true, affectedRows };
        }, cancellationToken, databaseName);
    }

    [McpServerTool(Destructive = true)]
    [Description("Invoke a stored procedure with typed Input, Output, InputOutput or ReturnValue parameters; return rows and outputParameters. Output directions support SQL Server/MySQL; ReturnValue supports SQL Server.")]
    public string CallStoredProcedure(
        [Description("Stored procedure name")] string procedureName,
        [Description("Typed input/output parameter array. NULL and output parameters require dbType; string/binary outputs require size.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON bytes. Oversized procedure results fail after execution; side effects are not rolled back.")] int? maxResponseBytes = null)
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

    [McpServerTool(Destructive = true)]
    [Description("Execute a SQL Server script that contains GO batches, automatically splitting the script and returning total affectedRows.")]
    public string ExecuteCommandWithGo(
        [Description("SQL script containing GO statements")] string sql,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
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

    [McpServerTool(Destructive = true)]
    [Description("Execute command objects sequentially over one connection; each object contains sql and optional typed parameters. Return success, affectedRows or error per command. Commands are not transactional; earlier writes remain committed on later failure or cancellation.")]
    public Task<string> BatchExecuteCommands(
        [Description("Nonempty array of {sql, parameters?} command objects. Each parameters value is a typed parameter array.")] DatabaseCommand[] commands,
        [Description("Optional SQL command timeout in seconds applied to every command in the batch. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
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
                            DatabaseAccessPolicy.EnsureCommandAllowed(context, command.Sql, ExecutionOptions.Access);
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
