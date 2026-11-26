using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 为单次 SQL 调用应用可选的 <see cref="IAdo.CommandTimeOut"/>。
/// 客户端由调用方独占租用；调用结束后恢复 timeout 状态。
/// </summary>
internal static class SqlCommandTimeout
{
    /// <summary>
    /// 允许的最大超时秒数（24 小时）。0 表示无限等待。
    /// </summary>

    public static T WithTimeout<T>(ISqlSugarClient db, int? commandTimeoutSeconds, Func<ISqlSugarClient, T> action)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(action);

        if (commandTimeoutSeconds is null)
        {
            return action(db);
        }

        CommandTimeoutPolicy.Validate(commandTimeoutSeconds.Value);

        var previousTimeout = db.Ado.CommandTimeOut;
        try
        {
            db.Ado.CommandTimeOut = commandTimeoutSeconds.Value;
            return action(db);
        }
        finally
        {
            db.Ado.CommandTimeOut = previousTimeout;
        }
    }

    public static async Task<T> WithTimeoutAsync<T>(ISqlSugarClient db, int? commandTimeoutSeconds,
        CancellationToken cancellationToken, Func<ISqlSugarClient, Task<T>> action)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Request/transaction executors validate public policy. A native fallback can be
        // one second longer than the request deadline, including at the public 24h ceiling.
        if (commandTimeoutSeconds is < 0) throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        var previousTimeout = db.Ado.CommandTimeOut;
        var previousToken = db.Ado.CancellationToken;
        try
        {
            if (commandTimeoutSeconds.HasValue) db.Ado.CommandTimeOut = commandTimeoutSeconds.Value;
            db.Ado.CancellationToken = cancellationToken;
            var result = await action(db).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            db.Ado.CommandTimeOut = previousTimeout;
            db.Ado.CancellationToken = previousToken;
        }
    }
}
