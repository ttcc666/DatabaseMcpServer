using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

/// <summary>Owns one result set's column names and reusable encoding buffers; returned rows own their bytes.</summary>
internal sealed class QueryRowEncoder : IDisposable
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IDataReader _reader;
    private readonly string[] _names;
    private readonly BudgetedJsonStream _stream;
    private readonly Utf8JsonWriter _writer;

    public QueryRowEncoder(IDataReader reader, string[]? names = null)
    {
        _reader = reader;
        _names = names ?? Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        if (names == null && _names.Distinct(StringComparer.Ordinal).Count() != _names.Length)
            throw new ArgumentException("Duplicate column name; specify SQL aliases.");
        _stream = new BudgetedJsonStream(long.MaxValue);
        _writer = new Utf8JsonWriter(_stream, new JsonWriterOptions { Encoder = Options.Encoder });
    }

    public async Task<BoundedJsonResult> ReadAsync(long maximumBytes, CancellationToken token,
        Dictionary<string, object?>? values = null)
    {
        values?.Clear();
        _stream.Reset(maximumBytes);
        _writer.Reset(_stream);
        try
        {
            _writer.WriteStartObject();
            for (var i = 0; i < _names.Length; i++)
            {
                var name = _names[i];
                if (name.Length > maximumBytes - _writer.BytesCommitted - _writer.BytesPending)
                    throw new ResponseBudgetExceededException();
                _writer.WritePropertyName(name);
                var remaining = maximumBytes - _writer.BytesCommitted - _writer.BytesPending;
                var value = await BudgetedFieldReader.ReadAsync(_reader, i, remaining, token);
                values?.Add(name, value);
                if (value is string text && text.Length > remaining
                    || value is byte[] bytes && ((long)bytes.Length + 2) / 3 * 4 > remaining)
                    throw new ResponseBudgetExceededException();
                JsonSerializer.Serialize(_writer, value == DBNull.Value ? null : value, Options);
                if (_writer.BytesCommitted + _writer.BytesPending > maximumBytes)
                    throw new ResponseBudgetExceededException();
            }
            _writer.WriteEndObject();
            _writer.Flush();
            return new(_stream.ToArray());
        }
        catch (ResponseBudgetExceededException)
        {
            values?.Clear();
            throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "字段或行超过响应预算。");
        }
        catch
        {
            values?.Clear();
            throw;
        }
        finally
        {
            // Discard unfinished writer bytes before disposal or the next row.
            _writer.Reset(_stream);
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }
}
