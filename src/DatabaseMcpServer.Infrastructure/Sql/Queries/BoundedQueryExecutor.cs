using System.Data;
using System.Text.Json;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

/// <summary>Budgets count returned rows and the tool's own UTF-8 JSON, excluding transport envelopes.</summary>
internal sealed class BoundedQueryExecutor
{
    private static readonly JsonSerializerOptions MetadataOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly int? _maxRows;
    private readonly int? _maxBytes;
    private long _remainingBytes;
    private int _rows;
    private bool _limitReached;
    private bool? _truncated = false;

    public int RowsRead => _rows;
    public long BytesConsumed => _maxBytes.HasValue ? _maxBytes.Value - _remainingBytes : 0;

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

    public async Task<DatabaseQueryResult<BoundedJsonResult>> QueryResultAsync(DatabaseClientContext context, string sql,
        SugarParameter[] parameters, CancellationToken cancellationToken, bool allResults = false)
    {
        await using var lease = await SqlSugarReaderAdapter.OpenAsync(context.Client, sql, parameters, cancellationToken, _maxBytes.HasValue);
        var result = await ReadResultAsync(context, lease.Reader, cancellationToken, allResults);
        if (!result.LimitReached) lease.Complete();
        return result;
    }

    public async Task<List<T>> ReadMetadataAsync<T>(ISqlSugarClient client, string sql, SugarParameter[] parameters, CancellationToken token)
    {
        await using var lease = await SqlSugarReaderAdapter.OpenAsync(client, sql, parameters, token);
        var reader = lease.Reader;
        using var encoder = new QueryRowEncoder(reader);
        var rows = new List<T>();
        while (await QueryReader.ReadAsync(reader, token))
        {
            try
            {
                if (_maxRows.HasValue && _rows >= _maxRows)
                    throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "元数据 section 超出行数或字节预算。");
                var row = await encoder.ReadAsync(_remainingBytes - 1, token);
                _remainingBytes -= row.Utf8Json.Length + 1;
                _rows++;
                rows.Add(JsonSerializer.Deserialize<T>(row.Utf8Json, MetadataOptions)!);
            }
            catch (DatabaseMcpException ex) when (ex.ErrorCode == DatabaseErrorCode.ResponseLimitExceeded)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "元数据 section 超出行数或字节预算。");
            }
        }
        lease.Complete();
        return rows;
    }

    public async Task<DatabaseQueryResult<BoundedJsonResult>> ReadResultAsync(DatabaseClientContext context, IDataReader reader,
        CancellationToken cancellationToken, bool allResults = false)
    {
        var rows = new List<BoundedJsonResult>();
        do
        {
            rows.AddRange(await ReadRowsAsync(reader, null, cancellationToken));
        } while (allResults && !_limitReached && await QueryReader.NextResultAsync(reader, cancellationToken));
        return new()
        {
            Success = true, DatabaseName = context.DatabaseName, DatabaseType = context.DatabaseType,
            ConfigurationVersion = context.ConfigurationVersion, RowCount = rows.Count,
            Data = rows,
            LimitReached = _limitReached, Truncated = _truncated
        };
    }

    public async Task<BoundedJsonResult> MultipleAsync(ISqlSugarClient client, string sql, SugarParameter[] parameters, CancellationToken cancellationToken = default, SqlSugar.DbType? product = null)
    {
        if (!DatabaseProviderCatalog.SupportsBoundedResults(product ?? client.CurrentConnectionConfig.DbType))
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability,
                "此 provider 尚未支持有限多结果集模式。");

        var sets = new List<object>();
        await using var lease = await SqlSugarReaderAdapter.OpenAsync(client, sql, parameters, cancellationToken, _maxBytes.HasValue);
        var reader = lease.Reader;
        do
        {
            if (_remainingBytes < 128)
            {
                Stop(true);
                break;
            }
            _remainingBytes -= 128;
            var names = GetColumnNames(reader);
            var rows = await ReadRowsAsync(reader, names, cancellationToken);
            sets.Add(new { rowCount = rows.Count, data = rows });
            if (_limitReached) break;
        } while (await QueryReader.NextResultAsync(reader, cancellationToken));
        if (!_limitReached) lease.Complete();
        return Finish(new { success = true, rowCount = _rows, resultSetCount = sets.Count, resultSets = sets,
            limitReached = _limitReached, truncated = _truncated });
    }

    public async Task<BoundedJsonResult> BatchAsync(ISqlSugarClient client, DatabaseCommand[] queries, Action<string> ensureSafeSql, CancellationToken cancellationToken = default)
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
                    results.Add(new { success = false, queryIndex = i, executed = false, reason = "budget_exhausted",
                        errorCode = DatabaseErrorCode.ResponseLimitExceeded, retryable = false, phase = "read" });
                    skipped++;
                    continue;
                }
                var rowsBeforeQuery = _rows;
                var bytesBeforeQuery = _remainingBytes;
                var executed = false;
                try
                {
                    ensureSafeSql(queries[i].Sql);
                    var parameters = SqlParameterBinder.Bind(queries[i].Parameters, client.CurrentConnectionConfig.DbType);
                    executed = true;
                    await using var lease = await SqlSugarReaderAdapter.OpenAsync(client, queries[i].Sql, parameters, cancellationToken, _maxBytes.HasValue);
                    var reader = lease.Reader;
                    var rows = await ReadRowsAsync(reader, null, cancellationToken);
                    if (!_limitReached) lease.Complete();
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
                    var failure = Services.DatabaseErrorMapper.Map(ex, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
                    results.Add(new { success = false, queryIndex = i, executed, error, errorCode = failure.ErrorCode,
                        retryable = failure.Retryable, phase = failure.Phase, capability = failure.Capability, hint = failure.Hint });
                    failed++;
                }
            }
        }
        return Finish(new { success = true, totalQueries = queries.Length, successfulQueries = successful,
            failedQueries = failed, skippedQueries = skipped, rowCount = _rows, results,
            limitReached = _limitReached, truncated = _truncated });
    }

    private async Task<List<BoundedJsonResult>> ReadRowsAsync(IDataReader reader, string[]? names, CancellationToken cancellationToken)
    {
        using var measurement = DatabaseTelemetry.MeasureRead();
        var rows = new List<BoundedJsonResult>();
        using var encoder = new QueryRowEncoder(reader, names);
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
                var encoded = await encoder.ReadAsync(_remainingBytes - 1, cancellationToken);
                _remainingBytes -= encoded.Utf8Json.Length + 1;
                rows.Add(encoded);
                _rows++;
            }
            catch (DatabaseMcpException ex) when (ex.ErrorCode == DatabaseErrorCode.ResponseLimitExceeded)
            {
                Stop(true);
                break;
            }
        }
        return rows;
    }

    private void Stop(bool? truncated)
    {
        _limitReached = true;
        _truncated = truncated;
    }

    private BoundedJsonResult Finish(object response)
    {
        using var measurement = DatabaseTelemetry.MeasureSerialization();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, QueryRowEncoder.Options);
        if (_maxBytes.HasValue && bytes.Length > _maxBytes.Value)
            throw new InvalidOperationException("响应元数据超出 maxResponseBytes。");
        return new(bytes);
    }

    public static BoundedJsonResult EncodeResponse(object response, int maximumBytes)
    {
        using var measurement = DatabaseTelemetry.MeasureSerialization();
        using var stream = new BudgetedJsonStream(maximumBytes);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = QueryRowEncoder.Options.Encoder });
        try
        {
            JsonSerializer.Serialize(writer, response, QueryRowEncoder.Options);
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

}
