using System.Data;
using System.Data.Common;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

/// <summary>Preserves SqlSugar preparation and callbacks while creating an exclusively owned reader lease.</summary>
internal static class SqlSugarReaderAdapter
{
    public static async Task<SqlReaderLease> OpenAsync(ISqlSugarClient client, string sql, SugarParameter[] parameters,
        CancellationToken token, bool sequential = true)
    {
        var config = client.CurrentConnectionConfig;
        var ado = (AdoProvider)client.Ado;
        if (!sequential || config.DbType is not (SqlSugar.DbType.PostgreSQL or SqlSugar.DbType.SqlServer)
            || config.SqlMiddle?.IsSqlMiddle == true || config.SlaveConnectionConfigs?.Count > 0
            || ado.Transaction != null || ado.CommandType != CommandType.Text
            || parameters.Any(p => p.Direction != ParameterDirection.Input && p.Direction != 0))
            return new(await ado.GetDataReaderAsync(sql, parameters));

        var previous = config.SqlMiddle;
        var previousToken = ado.CancellationToken;
        DbCommand? command = null;
        DbDataReader? nativeReader = null;
        try
        {
            ado.CancellationToken = token;
            config.SqlMiddle = new SqlMiddle
            {
                IsSqlMiddle = true,
                GetDataReaderAsync = async (preparedSql, preparedParameters) =>
                {
                    // The outer SqlSugar call already expanded IN arrays and formatted parameters.
                    // Restore middleware before invoking callbacks that may themselves execute SQL.
                    config.SqlMiddle = previous;
                    if (ado.ProcessingEventStartingSQL is { } transform)
                    {
                        var changed = transform(preparedSql, preparedParameters);
                        preparedSql = changed.Key;
                        preparedParameters = changed.Value;
                    }
                    ado.ExecuteBefore(preparedSql, preparedParameters);
                    // PostgreSQL's SqlSugar provider only implements GetCommand. Open first
                    // asynchronously so its synchronous connection check cannot block on I/O.
                    await ado.CheckConnectionAsync();
                    command = ado.GetCommand(preparedSql, preparedParameters);
                    var behavior = CommandBehavior.SequentialAccess;
                    if (config.IsAutoCloseConnection) behavior |= CommandBehavior.CloseConnection;
                    nativeReader = await command.ExecuteReaderAsync(behavior, token);
                    ado.ExecuteAfter(preparedSql, preparedParameters);
                    return nativeReader;
                }
            };
            var result = await ado.GetDataReaderAsync(sql, parameters);
            return new(result, command);
        }
        catch
        {
            if (nativeReader != null)
                await new SqlReaderLease(nativeReader, command).DisposeAsync();
            else
            {
                try { if (config.IsAutoCloseConnection) await ado.CloseAsync(); }
                finally { if (command != null) await command.DisposeAsync(); }
            }
            throw;
        }
        finally
        {
            config.SqlMiddle = previous;
            ado.CancellationToken = previousToken;
        }
    }
}
