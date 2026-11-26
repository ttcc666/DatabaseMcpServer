using System.Buffers;
using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DatabaseMcpServer.Helpers;

internal sealed class ResponseBudgetExceededException : Exception;

/// <summary>Bounds application-owned field values; drivers may still buffer a complete row.</summary>
internal static class BudgetedFieldReader
{
    public static ValueTask<object?> ReadAsync(IDataReader reader, int ordinal, long available, CancellationToken token)
    {
        if (reader is not Npgsql.NpgsqlDataReader and not Microsoft.Data.SqlClient.SqlDataReader)
            return ValueTask.FromResult(Read(reader, ordinal, available, token));
        return ReadStreamingAsync((System.Data.Common.DbDataReader)reader, ordinal, available, token);
    }

    private static async ValueTask<object?> ReadStreamingAsync(System.Data.Common.DbDataReader reader, int ordinal, long available, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (available < 0) throw new ResponseBudgetExceededException();
        if (await reader.IsDBNullAsync(ordinal, token)) return null;
        var type = reader.GetFieldType(ordinal);
        if (type == typeof(string))
        {
            var textReader = reader is Npgsql.NpgsqlDataReader postgres
                ? await postgres.GetTextReaderAsync(ordinal, token) : reader.GetTextReader(ordinal);
            try { return await ReadTextAsync(textReader, available, token); }
            finally
            {
                // Npgsql's StreamReader.Dispose drains unread network bytes synchronously.
                // Drain its owned stream asynchronously before disposing the text wrapper.
                try { if (textReader is StreamReader streaming) await streaming.BaseStream.DisposeAsync(); }
                finally { textReader.Dispose(); }
            }
        }
        if (type == typeof(byte[]))
        {
            await using var stream = reader is Npgsql.NpgsqlDataReader postgres
                ? await postgres.GetStreamAsync(ordinal, token) : reader.GetStream(ordinal);
            return await ReadBinaryAsync(stream, available, token);
        }
        return reader.GetValue(ordinal);
    }

    internal static async Task<string> ReadTextAsync(TextReader reader, long available, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (available < 2) throw new ResponseBudgetExceededException();
        // One extra character lets a full first chunk probe EOF without allocating a builder.
        var buffer = ArrayPool<char>.Shared.Rent(1025);
        try
        {
            StringBuilder? text = null;
            var buffered = 0;
            while (true)
            {
                var length = text?.Length ?? buffered;
                var offset = text == null ? buffered : 0;
                var capacity = text == null ? 1025 - buffered : 1024;
                var requested = (int)Math.Min(Math.Min(1024, capacity), Math.Max(1, available - 1 - length));
                var count = await reader.ReadAsync(buffer.AsMemory(offset, requested), token);
                token.ThrowIfCancellationRequested();
                if (count == 0) return text?.ToString() ?? new string(buffer, 0, buffered);
                if ((long)length + count + 2 > available) throw new ResponseBudgetExceededException();
                if (text != null) text.Append(buffer, 0, count);
                else
                {
                    buffered += count;
                    if (buffered > 1024)
                    {
                        text = new StringBuilder(buffered);
                        text.Append(buffer, 0, buffered);
                        buffered = 0;
                    }
                }
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    internal static async Task<byte[]> ReadBinaryAsync(Stream stream, long available, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (available < 2) throw new ResponseBudgetExceededException();
        var maximum = Math.Min(int.MaxValue, (available - 2) / 4 * 3);
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        using var value = new MemoryStream();
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(8192, maximum - value.Length + 1)), token);
                token.ThrowIfCancellationRequested();
                if (count == 0) return value.ToArray();
                if (value.Length + count > maximum) throw new ResponseBudgetExceededException();
                value.Write(buffer, 0, count);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static object? Read(IDataReader reader, int ordinal, long available, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (available < 0) throw new ResponseBudgetExceededException();
        // Sqlite GetTextReader/GetBytes can first copy the whole value into a MemoryStream.
        // Its public statement handle lets us check native UTF-8/blob size before that copy.
        if (reader is SqliteDataReader sqlite)
        {
            var sqliteType = SQLitePCL.raw.sqlite3_column_type(sqlite.Handle!, ordinal);
            if (sqliteType == SQLitePCL.raw.SQLITE_NULL) return null;
            if (sqliteType is SQLitePCL.raw.SQLITE_TEXT or SQLitePCL.raw.SQLITE_BLOB && available < int.MaxValue)
            {
                var length = SQLitePCL.raw.sqlite3_column_bytes(sqlite.Handle!, ordinal);
                var minimum = sqliteType == SQLitePCL.raw.SQLITE_BLOB ? ((long)length + 2) / 3 * 4 + 2 : (long)length + 2;
                if (minimum > available) throw new ResponseBudgetExceededException();
            }
            return reader.GetValue(ordinal);
        }
        if (reader.IsDBNull(ordinal)) return null;
        var type = reader.GetFieldType(ordinal);
        if (type != typeof(string) && type != typeof(byte[]) || available >= int.MaxValue) return reader.GetValue(ordinal);
        if (type == typeof(byte[]))
        {
            long length;
            try { length = reader.GetBytes(ordinal, 0, null, 0, 0); }
            catch (NotSupportedException) { return reader.GetValue(ordinal); }
            if (length > int.MaxValue || (length + 2) / 3 * 4 + 2 > available)
                throw new ResponseBudgetExceededException();
            var value = new byte[(int)length];
            var position = 0;
            while (position < value.Length)
            {
                token.ThrowIfCancellationRequested();
                var count = (int)reader.GetBytes(ordinal, position, value, position, Math.Min(8192, value.Length - position));
                if (count == 0) throw new EndOfStreamException("Binary field ended before its declared length.");
                position += count;
            }
            return value;
        }

        // MySqlConnector text APIs materialize GetString. Unknown providers retain
        // their native scalar behavior until streaming is verified.
        return reader.GetValue(ordinal);
    }
}
