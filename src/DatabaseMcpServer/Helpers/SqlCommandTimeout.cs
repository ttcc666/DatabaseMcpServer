using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 为单次 SQL 调用应用可选的 <see cref="IAdo.CommandTimeOut"/>。
/// 指定超时时使用 <c>CopyNew()</c> 隔离客户端，避免污染共享连接池中的 timeout 状态。
/// </summary>
internal static class SqlCommandTimeout
{
    /// <summary>
    /// 允许的最大超时秒数（24 小时）。0 表示无限等待。
    /// </summary>
    public const int MaxTimeoutSeconds = 86_400;

    public static T WithTimeout<T>(ISqlSugarClient db, int? commandTimeoutSeconds, Func<ISqlSugarClient, T> action)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(action);

        if (commandTimeoutSeconds is null)
        {
            return action(db);
        }

        Validate(commandTimeoutSeconds.Value);

        var isolated = db.CopyNew();
        try
        {
            isolated.Ado.CommandTimeOut = commandTimeoutSeconds.Value;
            return action(isolated);
        }
        finally
        {
            isolated.Dispose();
        }
    }

    public static void Validate(int commandTimeoutSeconds)
    {
        if (commandTimeoutSeconds is < 0 or > MaxTimeoutSeconds)
        {
            throw new DatabaseMcpException(
                DatabaseErrorCode.InvalidParameters,
                $"commandTimeoutSeconds 必须在 0-{MaxTimeoutSeconds} 之间（0 表示无限等待）");
        }
    }
}
