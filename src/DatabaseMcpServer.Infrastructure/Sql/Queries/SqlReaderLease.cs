using System.Data;
using System.Data.Common;

namespace DatabaseMcpServer.Helpers;

/// <summary>Retains the native command until its reader is disposed; the client is exclusively leased.</summary>
internal sealed class SqlReaderLease(IDataReader reader, DbCommand? command = null) : IAsyncDisposable
{
    public IDataReader Reader { get; } = reader;
    public bool Sequential => command != null;
    private bool _complete;

    public void Complete() => _complete = true;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (command != null && !_complete)
            {
                try { command.Cancel(); }
                catch (Exception ex) when (ex is DbException or InvalidOperationException) { }
            }
            if (Reader is DbDataReader dbReader) await dbReader.DisposeAsync();
            else Reader.Dispose();
        }
        finally { if (command != null) await command.DisposeAsync(); }
    }
}
