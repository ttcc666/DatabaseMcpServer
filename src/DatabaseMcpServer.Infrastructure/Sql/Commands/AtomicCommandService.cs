using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

internal sealed class AtomicCommandService(SqlDatabaseSessions sessions, DatabaseExecutionOptions options, DatabaseMetadataCache cache,
    AtomicTransactionCompletion completion, IDatabaseHelperService helper, ILogger<AtomicCommandService> logger)
{
    public async Task<object> Execute(DatabaseCommand[] commands, string? databaseName, int? commandTimeoutSeconds,
        int totalTimeoutSeconds, CancellationToken cancellationToken)
    {
        var result = new AtomicBatchResult();
        DatabaseClientLease? lease = null;
        DbTransaction? transaction = null;
        var commitStarted = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            if (commands is not { Length: > 0 and <= 100 } || commands.Any(c => c == null || string.IsNullOrWhiteSpace(c.Sql)))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "atomic commands 必须包含 1-100 条非空 DML。");
            if (JsonSerializer.SerializeToUtf8Bytes(commands).Length > 4 * 1024 * 1024)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "atomic 输入超过 4 MiB。");
            if (totalTimeoutSeconds is < 1 or > 300 || totalTimeoutSeconds > options.MaxCommandTimeoutSeconds)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "totalTimeoutSeconds 必须在 1-300 之间且符合服务端上限。");
            var perCommandTimeout = options.ResolveTimeout(commandTimeoutSeconds);
            foreach (var command in commands) SqlSafetyGuard.EnsureAtomicDml(command.Sql);
            result.TotalCommands = commands.Length;
            result.Results = Enumerable.Range(0, commands.Length).Select(i => new AtomicCommandResult { CommandIndex = i }).ToList();
            deadline.CancelAfter(TimeSpan.FromSeconds(totalTimeoutSeconds));
            var started = Stopwatch.GetTimestamp();
            lease = databaseName == null ? await sessions.AcquireClientAsync(deadline.Token) : await sessions.AcquireClientAsync(databaseName, deadline.Token);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, DatabaseToolAccess.Write, options.Access);
            var db = lease.Client;
            var provider = db.CurrentConnectionConfig.DbType;
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(lease.Context.DatabaseType), DatabaseCapability.AtomicDml);
            foreach (var command in commands)
            {
                SqlAccessPolicy.EnsureCommandAllowed(lease.Context, command.Sql, options.Access);
                if (!lease.Context.EnableDangerousOperations && helper.DetectDangerousOperation(command.Sql))
                    throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到危险操作；atomic 不绕过 enableDangerousOperations。");
            }
            var bound = commands.Select(c => SqlParameterBinder.Bind(c.Parameters, provider)).ToArray();
            if (db.Ado.Connection is not DbConnection connection)
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "provider 不支持可取消事务。");
            await connection.OpenAsync(deadline.Token);
            transaction = await connection.BeginTransactionAsync(deadline.Token);
            db.Ado.Transaction = transaction;
            result.TransactionOutcome = "active";
            for (var i = 0; i < commands.Length; i++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var remaining = Math.Max(1, (int)Math.Ceiling(totalTimeoutSeconds - Stopwatch.GetElapsedTime(started).TotalSeconds));
                var timeout = perCommandTimeout is > 0 ? Math.Min(remaining, perCommandTimeout.Value) : remaining;
                var item = result.Results[i];
                item.Executed = true;
                item.Outcome = "executed";
                item.AffectedRows = await SqlCommandTimeout.WithTimeoutAsync(db, timeout, deadline.Token,
                    client => client.Ado.ExecuteCommandAsync(commands[i].Sql, bound[i]));
            }
            deadline.Token.ThrowIfCancellationRequested();
            commitStarted = true;
            await completion.CommitAsync(transaction, deadline.Token);
            result.TransactionOutcome = "committed";
            result.Success = true;
            result.CommittedAffectedRows = result.Results.Sum(r => (long)Math.Max(0, r.AffectedRows ?? 0));
            foreach (var item in result.Results) item.Outcome = "committed";
        }
        catch (Exception ex)
        {
            result.Cancelled = cancellationToken.IsCancellationRequested;
            result.ErrorCode = (int)(ex is DatabaseMcpException known ? known.ErrorCode : deadline.IsCancellationRequested ? DatabaseErrorCode.CommandTimeout : DatabaseErrorCode.QueryExecutionFailed);
            result.ErrorMessage = ex is DatabaseMcpException valid ? valid.Message : "原子批处理未获成功确认；请根据 transactionOutcome 核实结果，不要自动重放。";
            var details = ApiResult<object>.CreateError(result.ErrorMessage, (DatabaseErrorCode)result.ErrorCode);
            result.Retryable = false;
            result.Phase = commitStarted ? "commit" : transaction != null ? "execution" : details.Phase;
            result.Capability = (ex as DatabaseMcpException)?.Capability;
            result.Hint = details.Hint;
            if (transaction != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await completion.RollbackAsync(transaction, cleanup.Token);
                    result.TransactionOutcome = commitStarted ? "unknown" : "rolled-back";
                }
                catch (Exception rollbackError)
                {
                    result.TransactionOutcome = "unknown";
                    LogCleanupFailure("rollback", rollbackError, result.TransactionOutcome);
                }
                foreach (var item in result.Results.Where(r => r.Executed))
                {
                    item.Outcome = result.TransactionOutcome;
                    item.AffectedRows = null;
                }
            }
        }
        finally
        {
            // Preserve a known commit/rollback outcome even if client cleanup fails afterwards.
            if (lease != null)
            {
                try
                {
                    lease.Client.Ado.Transaction = null;
                    if (result.TransactionOutcome == "unknown")
                    {
                        switch (lease.Client.Ado.Connection)
                        {
                            case Microsoft.Data.SqlClient.SqlConnection sql: Microsoft.Data.SqlClient.SqlConnection.ClearPool(sql); break;
                            case Npgsql.NpgsqlConnection pg: Npgsql.NpgsqlConnection.ClearPool(pg); break;
                            case Microsoft.Data.Sqlite.SqliteConnection sqlite: Microsoft.Data.Sqlite.SqliteConnection.ClearPool(sqlite); break;
                            case MySqlConnector.MySqlConnection mysql: MySqlConnector.MySqlConnection.ClearPool(mysql); break;
                            case Oracle.ManagedDataAccess.Client.OracleConnection oracle: Oracle.ManagedDataAccess.Client.OracleConnection.ClearPool(oracle); break;
                            case Kdbndp.KdbndpConnection kdb: Kdbndp.KdbndpConnection.ClearPool(kdb); break;
                            case OpenGauss.NET.OpenGaussConnection og: OpenGauss.NET.OpenGaussConnection.ClearPool(og); break;
                            case Nhgdb.NhgdbConnection hg: Nhgdb.NhgdbConnection.ClearPool(hg); break;
                        }
                    }
                }
                catch (Exception cleanupError)
                {
                    LogCleanupFailure("connection", cleanupError, result.TransactionOutcome);
                }
                try { if (transaction != null) await transaction.DisposeAsync(); }
                catch (Exception cleanupError)
                {
                    LogCleanupFailure("transaction-dispose", cleanupError, result.TransactionOutcome);
                }
                cache.Invalidate(lease.Context);
                try { lease.Dispose(); }
                catch (Exception cleanupError)
                {
                    LogCleanupFailure("lease-dispose", cleanupError, result.TransactionOutcome);
                }
            }
        }
        return result;
    }

    // Driver exception messages may contain SQL or connection credentials.
    private void LogCleanupFailure(string phase, Exception error, string outcome)
        => logger.LogWarning("原子批处理清理失败：{Phase}，{ExceptionType} ({ErrorCode})，transactionOutcome={TransactionOutcome}。",
            phase, error.GetType().FullName, error.HResult, outcome);
}
