using System.Data;
using System.Data.Common;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class QueryReader
{
    public static async ValueTask<bool> ReadAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = reader is DbDataReader dbReader
            ? await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false)
            : reader.Read();
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public static async ValueTask<bool> NextResultAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = reader is DbDataReader dbReader
            ? await dbReader.NextResultAsync(cancellationToken).ConfigureAwait(false)
            : reader.NextResult();
        cancellationToken.ThrowIfCancellationRequested();
        return next;
    }

}
