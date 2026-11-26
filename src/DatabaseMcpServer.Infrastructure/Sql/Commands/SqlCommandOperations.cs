using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库命令执行工具类，用于执行数据库的增删改操作、存储过程和事务。
/// </summary>
internal class SqlCommandOperations : SqlOperationExecutor
{
    private readonly AtomicCommandService _atomic;
    public SqlCommandOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SqlCommandOperations> logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions, AtomicCommandService atomic)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
        _atomic = atomic;
    }

    protected override DatabaseToolAccess RequiredAccess => DatabaseToolAccess.Write;

    public Task<object> InsertRow(string tableName, JsonElement row, string? databaseName = null,
        int? commandTimeoutSeconds = null, CancellationToken cancellationToken = default)
        => WithDeadlineAsync(async (context, token) =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.InsertIdentity);
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            if (tableName.Contains('.'))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "insert_row 仅支持普通表名；当前 SqlSugar 内置元数据不支持限定 schema。请使用 execute_command。");
            if (row.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(row.GetRawText()) > 4 * 1024 * 1024)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "row 必须为不超过 4 MiB 的 JSON 对象。");
            var values = new Dictionary<string, object>();
            foreach (var field in row.EnumerateObject())
            {
                SqlSafetyGuard.EnsureSafeTableName(field.Name);
                if (field.Name.Contains('.') || field.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object ||
                    !values.TryAdd(field.Name, JsonElementValueConverter.ConvertToValue(field.Value)!))
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "row 仅接受不重复的普通字段名及 string/bool/number/null 值。");
            }
            if (values.Count is 0 or > 128)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "row 必须包含 1-128 个字段。");

            var db = context.Client;
            token.ThrowIfCancellationRequested();
            if (db.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer &&
                db.DbMaintenance.GetTableInfoList(false).Count(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "表不存在或跨 schema 同名；SqlSugar 无法唯一识别目标表，请使用 execute_command。");
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName, false);
            var identities = columns.Where(c => c.IsIdentity).ToArray();
            if (identities.Length != 1 || identities[0].DataType.ToLowerInvariant() is not
                ("tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint"))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "表必须具有 SqlSugar 可识别的唯一整数自增列；SQLite 需要 AUTOINCREMENT。");
            var identity = identities[0];
            foreach (var name in values.Keys)
            {
                if (name == identity.DbColumnName || !columns.Any(c => c.DbColumnName == name))
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "row 字段必须存在（区分大小写），且不能指定自增列。");
            }
            token.ThrowIfCancellationRequested();
            var insertedId = await db.Insertable(values).AS(tableName).ExecuteReturnBigIdentityAsync(token);
            return new
            {
                success = true, databaseName = context.DatabaseName, configurationVersion = context.ConfigurationVersion,
                tableName, identityColumn = identity.DbColumnName, identity = insertedId.ToString(CultureInfo.InvariantCulture)
            };
        }, commandTimeoutSeconds, cancellationToken, databaseName);

    public Task<object> ExecuteCommand(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
    {
        return WithDeadlineAsync(async (context, token) =>
        {
            EnsureSafeSql(sql, context.EnableDangerousOperations);
            SqlAccessPolicy.EnsureCommandAllowed(context, sql, ExecutionOptions.Access);
            var parsedParams = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
            var affectedRows = await context.Client.Ado.ExecuteCommandAsync(sql, parsedParams);

            return new { success = true, affectedRows };
        }, commandTimeoutSeconds, cancellationToken, databaseName);
    }

    public Task<object> CallStoredProcedure(
        string procedureName,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        string? databaseName = null,
        int? maxResponseBytes = null, int? maxRows = null, CancellationToken cancellationToken = default)
    {
        return WithDeadlineAsync(async (context, token) =>
        {
            var db = context.Client;
            DatabaseAccessPolicy.EnsureAllowed(context, DatabaseToolAccess.Schema, ExecutionOptions.Access);
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.StoredProcedures);
            SqlSafetyGuard.EnsureSafeTableName(procedureName);
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            var sugarParams = SqlParameterBinder.Bind(parameters, db.CurrentConnectionConfig.DbType, storedProcedure: true);

            var previousType = db.Ado.CommandType;
            try
            {
                db.Ado.CommandType = System.Data.CommandType.StoredProcedure;
                await using var command = await ((SqlSugar.AdoProvider)db.Ado).GetCommandAsync(procedureName, sugarParams);
                DatabaseQueryResult<BoundedJsonResult> result;
                await using (var reader = await command.ExecuteReaderAsync(db.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer
                    ? System.Data.CommandBehavior.SequentialAccess : System.Data.CommandBehavior.Default, token))
                {
                    result = await new BoundedQueryExecutor(limits.Rows, limits.Bytes, 1024).ReadResultAsync(context, reader, token, allResults: true);
                    if (result.LimitReached)
                    {
                        command.Cancel();
                        throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded,
                            "存储过程结果达到预算，输出参数不可用；已发生的副作用不会回滚。请缩小结果或增加预算。");
                    }
                }
                var outputValues = command.Parameters.Cast<System.Data.Common.DbParameter>()
                    .Where(p => p.Direction != System.Data.ParameterDirection.Input)
                    .ToDictionary(p => p.ParameterName, p => p.Value == DBNull.Value ? null : p.Value);
                return BoundedQueryExecutor.EncodeResponse(new
                {
                    success = true,
                    rowCount = result.RowCount,
                    data = result.Data,
                    outputParameters = outputValues
                }, limits.Bytes!.Value);
            }
            finally { db.Ado.CommandType = previousType; }
        }, commandTimeoutSeconds, cancellationToken, databaseName);
    }

    public object ExecuteCommandWithGo(
        string sql,
        int? commandTimeoutSeconds = null,
        string? databaseName = null)
    {
        return WithClientContext(context =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.SqlServerBatches);
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

    public async Task<object> BatchExecuteCommands(
        DatabaseCommand[] commands,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null,
        bool atomic = false, int totalTimeoutSeconds = 30)
    {
        if (atomic) return await _atomic.Execute(commands, databaseName, commandTimeoutSeconds, totalTimeoutSeconds, cancellationToken);

        var totalCommands = commands?.Length ?? 0;
        var results = new List<object>();
        int? unknownCommandIndex = null;
        var response = await WithDeadlineAsync(async (context, token) =>
        {
            if (commands == null || totalCommands == 0 || commands.Any(command => command == null || string.IsNullOrWhiteSpace(command.Sql)))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 必须是包含非空 sql 的对象数组。");

            var client = context.Client;
            using (client.Ado.OpenAlways())
            {
                for (var i = 0; i < totalCommands; i++)
                {
                    token.ThrowIfCancellationRequested();
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
                        token.ThrowIfCancellationRequested();
                        unknownCommandIndex = i;
                        var affectedRows = await client.Ado.ExecuteCommandAsync(command.Sql, parameters);
                        results.Add(new { success = true, affectedRows, commandIndex = i });
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested && ex is not OperationCanceledException)
                    {
                        results.Add(new { success = false, error = ex.Message, commandIndex = i });
                    }
                    unknownCommandIndex = null;
                }
            }

            return new
            {
                success = true,
                totalCommands,
                results
            };
        }, commandTimeoutSeconds, cancellationToken, databaseName);

        if (response is ApiResult<object> { ErrorCode: DatabaseErrorCode.CommandTimeout } timeout)
        {
            var nextCommandIndex = unknownCommandIndex is int inFlight ? inFlight + 1 : results.Count;
            timeout.Data = new
            {
                totalCommands,
                results,
                unknownCommandIndex,
                notExecutedFromIndex = nextCommandIndex < totalCommands ? (int?)nextCommandIndex : null
            };
            if (unknownCommandIndex.HasValue)
                timeout.Hint = "请先核对结果未知的命令是否已写入，再决定如何继续。";
        }

        return response;
    }

    private void EnsureSafeSql(string sql, bool enableDangerousOperations)
    {
        if (!enableDangerousOperations && DatabaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到危险操作。请使用特定工具进行架构操作，或在当前连接配置中显式设置 enableDangerousOperations=true。");
        }
    }
}
