using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

/// <summary>Budgets count returned rows and the tool's own UTF-8 JSON, excluding transport envelopes.</summary>
internal sealed class BoundedQueryExecutor
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly int? _maxRows;
    private readonly int? _maxBytes;
    private long _remainingBytes;
    private int _rows;
    private bool _limitReached;
    private bool? _truncated = false;

    public BoundedQueryExecutor(int? maxRows, int? maxResponseBytes, int reservedBytes = 256)
    {
        if (maxRows is <= 0 || maxResponseBytes is <= 0)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "maxRows 和 maxResponseBytes 必须为正整数。");
        if (maxResponseBytes < reservedBytes)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters,
                $"maxResponseBytes 至少需要 {reservedBytes} 字节以容纳响应元数据。");
        _maxRows = maxRows;
        _maxBytes = maxResponseBytes;
        _remainingBytes = (long?)maxResponseBytes - reservedBytes ?? long.MaxValue;
    }

    public async Task<DatabaseQueryResult> QueryResultAsync(DatabaseClientContext context, string sql,
        SugarParameter[] parameters, CancellationToken cancellationToken)
    {
        using var reader = await context.Client.Ado.GetDataReaderAsync(sql, parameters);
        var rows = await ReadRowsAsync(reader, () => (IDictionary<string, object?>)context.Client.Utilities.DataReaderToExpandoObject(reader), cancellationToken);
        return new()
        {
            Success = true, DatabaseName = context.DatabaseName, DatabaseType = context.DatabaseType,
            ConfigurationVersion = context.ConfigurationVersion, RowCount = rows.Count,
            Data = rows.Select(row => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Utf8Json)!).ToList(),
            LimitReached = _limitReached, Truncated = _truncated
        };
    }

    public async Task<BoundedJsonResult> MultipleAsync(ISqlSugarClient client, string sql, SugarParameter[] parameters, CancellationToken cancellationToken = default)
    {
        if (!DatabaseCapabilities.SupportsBoundedResults(client.CurrentConnectionConfig.DbType))
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability,
                "此 provider 尚未支持有限多结果集模式。");

        var sets = new List<object>();
        using var reader = await client.Ado.GetDataReaderAsync(sql, parameters);
        do
        {
            if (_remainingBytes < 128)
            {
                Stop(true);
                break;
            }
            _remainingBytes -= 128;
            var names = GetColumnNames(reader);
            var rows = await ReadRowsAsync(reader, () =>
            {
                var row = new Dictionary<string, object?>(names.Length);
                for (var i = 0; i < names.Length; i++) row[names[i]] = reader.GetValue(i);
                return row;
            }, cancellationToken);
            sets.Add(new { rowCount = rows.Count, data = rows });
            if (_limitReached) break;
        } while (await QueryReader.NextResultAsync(reader, cancellationToken));
        return Finish(new { success = true, rowCount = _rows, resultSetCount = sets.Count, resultSets = sets,
            limitReached = _limitReached, truncated = _truncated });
    }

    public async Task<BoundedJsonResult> BatchAsync(ISqlSugarClient client, string[] queries, Action<string> ensureSafeSql, CancellationToken cancellationToken = default)
    {
        var results = new List<object>();
        var successful = 0;
        var failed = 0;
        var skipped = 0;
        using (client.Ado.OpenAlways())
        {
            for (var i = 0; i < queries.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_limitReached)
                {
                    results.Add(new { success = false, queryIndex = i, executed = false, reason = "budget_exhausted" });
                    skipped++;
                    continue;
                }
                var rowsBeforeQuery = _rows;
                var bytesBeforeQuery = _remainingBytes;
                var executed = false;
                try
                {
                    ensureSafeSql(queries[i]);
                    executed = true;
                    using var reader = await client.Ado.GetDataReaderAsync(queries[i]);
                    var rows = await ReadRowsAsync(reader, () => (IDictionary<string, object?>)client.Utilities.DataReaderToExpandoObject(reader), cancellationToken);
                    results.Add(new { success = true, queryIndex = i, executed = true, rowCount = rows.Count, data = rows });
                    successful++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
                {
                    // A failed item returns no partial data; restore its shared budget as well.
                    _rows = rowsBeforeQuery;
                    _remainingBytes = bytesBeforeQuery;
                    // Error text is bounded too; the original exception is not serialized as a large object graph.
                    var error = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                    results.Add(new { success = false, queryIndex = i, executed, error });
                    failed++;
                }
            }
        }
        return Finish(new { success = true, totalQueries = queries.Length, successfulQueries = successful,
            failedQueries = failed, skippedQueries = skipped, rowCount = _rows, results,
            limitReached = _limitReached, truncated = _truncated });
    }

    private async Task<List<BoundedJsonResult>> ReadRowsAsync(IDataReader reader, Func<IDictionary<string, object?>> mapRow, CancellationToken cancellationToken)
    {
        using var measurement = DatabaseTelemetry.MeasureRead();
        var rows = new List<BoundedJsonResult>();
        using var stream = new BudgetStream(long.MaxValue);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Options.Encoder });
        while (true)
        {
            if (_maxRows.HasValue && _rows >= _maxRows.Value)
            {
                Stop(null); // Do not read row N+1 just to determine whether it exists.
                break;
            }
            if (!await QueryReader.ReadAsync(reader, cancellationToken)) break;
            try
            {
                var encoded = EncodeRow(mapRow(), _remainingBytes - 1, stream, writer);
                _remainingBytes -= encoded.Length + 1;
                rows.Add(new(encoded));
                _rows++;
            }
            catch (ResponseBudgetExceededException)
            {
                // Reset also discards pending writer bytes before Dispose can flush an incomplete row.
                writer.Reset(stream);
                Stop(true);
                break;
            }
        }
        return rows;
    }

    private static byte[] EncodeRow(IDictionary<string, object?> row, long available, BudgetStream stream, Utf8JsonWriter writer)
    {
        stream.Reset(available);
        writer.Reset(stream);
        writer.WriteStartObject();
        foreach (var (name, value) in row)
        {
            if (name.Length > available - writer.BytesCommitted - writer.BytesPending) throw new ResponseBudgetExceededException();
            writer.WritePropertyName(name);
            var remaining = available - writer.BytesCommitted - writer.BytesPending;
            if (value is string text && text.Length > remaining
                || value is byte[] bytes && ((long)bytes.Length + 2) / 3 * 4 > remaining)
                throw new ResponseBudgetExceededException();
            JsonSerializer.Serialize(writer, value == DBNull.Value ? null : value, Options);
            if (writer.BytesCommitted + writer.BytesPending > available) throw new ResponseBudgetExceededException();
        }
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private void Stop(bool? truncated)
    {
        _limitReached = true;
        _truncated = truncated;
    }

    private BoundedJsonResult Finish(object response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        if (_maxBytes.HasValue && bytes.Length > _maxBytes.Value)
            throw new InvalidOperationException("响应元数据超出 maxResponseBytes。");
        return new(bytes);
    }

    public static BoundedJsonResult EncodeResponse(object response, int maximumBytes)
    {
        using var stream = new BudgetStream(maximumBytes);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Options.Encoder });
        try
        {
            JsonSerializer.Serialize(writer, response, Options);
            writer.Flush();
            return new(stream.ToArray());
        }
        catch (ResponseBudgetExceededException)
        {
            writer.Reset(stream);
            throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded,
                "单值或响应超出 maxResponseBytes；请在 SQL 中缩小字段或增加预算。");
        }
    }

    private static string[] GetColumnNames(IDataReader reader)
    {
        var names = new string[reader.FieldCount];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i).Trim();
            if (name.Length == 0) name = $"Column{i + 1}";
            var unique = name;
            var suffix = i;
            while (!used.Add(unique)) unique = name + suffix++;
            names[i] = unique;
        }
        return names;
    }

    private sealed class ResponseBudgetExceededException : Exception;

    private sealed class BudgetStream(long maximum) : Stream
    {
        private long _maximum = maximum;
        private readonly MemoryStream _buffer = new();
        public void Reset(long limit)
        {
            _maximum = limit;
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }
        public byte[] ToArray() => _buffer.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > _maximum) throw new ResponseBudgetExceededException();
            _buffer.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}

[JsonConverter(typeof(BoundedJsonResultConverter))]
internal sealed record BoundedJsonResult(byte[] Utf8Json);

internal sealed class BoundedJsonResultConverter : JsonConverter<BoundedJsonResult>
{
    public override BoundedJsonResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, BoundedJsonResult value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.Utf8Json, skipInputValidation: true);
}
