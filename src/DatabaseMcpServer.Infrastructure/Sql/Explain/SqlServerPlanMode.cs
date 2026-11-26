using System.Data;
using System.Data.Common;

namespace DatabaseMcpServer.Helpers;

internal class SqlServerPlanMode
{
    public virtual async Task SetAsync(DbConnection connection, bool enabled, int timeout, CancellationToken token)
    {
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = enabled ? "SET SHOWPLAN_XML ON" : "SET SHOWPLAN_XML OFF";
        command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(token);
    }
}
