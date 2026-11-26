using System.Collections;
using System.Data.Common;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class BudgetedFieldReaderTests
{
    [Fact]
    public async Task TextRead_PreservesPendingCompletionAndEmptyBoundary()
    {
        using var reader = new DelayedTextReader("汉😀");
        async Task<string> Read() => await BudgetedFieldReader.ReadTextAsync(reader, 5, default);
        var read = Read();
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(read.IsCompleted);
        reader.Release.TrySetResult();
        Assert.Equal("汉😀", await read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("", await BudgetedFieldReader.ReadTextAsync(new StringReader(""), 2, default));
        await Assert.ThrowsAsync<ResponseBudgetExceededException>(async () =>
            await BudgetedFieldReader.ReadTextAsync(new StringReader(""), 1, default));
    }

    private sealed class DelayedTextReader(string text) : StringReader(text)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1024)]
    public async Task ShortText_PreservesPartialReadsAndSurrogateBoundary(int chunkSize)
    {
        foreach (var prefix in new[] { 127, 1023, 1024 })
        {
            var text = new string('x', prefix) + "😀汉\"\\\n";
            using var reader = new ChunkedTextReader(text, chunkSize);
            Assert.Equal(text, await BudgetedFieldReader.ReadTextAsync(reader, text.Length + 2, default));
        }
    }

    [Fact]
    public async Task ExactTextBudget_ObservesCancellationDuringFinalProbe()
    {
        using var reader = new PendingEofReader("short汉😀");
        using var cancellation = new CancellationTokenSource();
        var read = BudgetedFieldReader.ReadTextAsync(reader, 10, cancellation.Token);
        await reader.Probing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(read.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class ChunkedTextReader(string text, int chunkSize) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    }

    private sealed class PendingEofReader(string text) : StringReader(text)
    {
        public TaskCompletionSource Probing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read != 0) return read;
            Probing.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task AsyncFields_PreserveBoundaryValuesAndRejectOversizedPayloads()
    {
        var text = new string('x', 1023) + "😀汉\"\\\n";
        Assert.Equal(text, await BudgetedFieldReader.ReadTextAsync(new StringReader(text), 8192, default));
        Assert.Equal("abc", await BudgetedFieldReader.ReadTextAsync(new StringReader("abc"), 5, default));
        await Assert.ThrowsAsync<ResponseBudgetExceededException>(() =>
            BudgetedFieldReader.ReadTextAsync(new StringReader("abc"), 4, default));
        foreach (var length in new[] { 0, 1, 2, 3, 4, 8193 })
        {
            var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            var exact = ((long)length + 2) / 3 * 4 + 2;
            using var source = new MemoryStream(bytes);
            Assert.Equal(bytes, await BudgetedFieldReader.ReadBinaryAsync(source, exact, default));
            source.Position = 0;
            await Assert.ThrowsAsync<ResponseBudgetExceededException>(() => BudgetedFieldReader.ReadBinaryAsync(source, exact - 1, default));
        }
    }

    [Fact]
    public async Task AsyncFields_CancelWhileUnderlyingReadIsPending()
    {
        using var textCancellation = new CancellationTokenSource();
        using var text = new PendingTextReader();
        var textRead = BudgetedFieldReader.ReadTextAsync(text, 8192, textCancellation.Token);
        await text.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(textRead.IsCompleted);
        textCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => textRead.WaitAsync(TimeSpan.FromSeconds(5)));

        using var binaryCancellation = new CancellationTokenSource();
        using var binary = new PendingStream();
        var binaryRead = BudgetedFieldReader.ReadBinaryAsync(binary, 8192, binaryCancellation.Token);
        await binary.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(binaryRead.IsCompleted);
        binaryCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => binaryRead.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class PendingTextReader : StringReader
    {
        public PendingTextReader() : base("") { }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class PendingStream : MemoryStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task TextBudget_AcceptsExactCharactersAndQuotesButRejectsOneByteLess()
    {
        foreach (var length in new[] { 0, 1, 1024, 1025 })
        {
            await Assert.ThrowsAsync<ResponseBudgetExceededException>(() => BudgetedFieldReader.ReadTextAsync(new GeneratedTextReader(length), length + 1, default));
            Assert.Equal(new string('x', length), await BudgetedFieldReader.ReadTextAsync(new GeneratedTextReader(length), length + 2, default));
        }
    }

    [Fact]
    public async Task DriverConversionFailure_IsNotSilentlyReturnedAsNull()
    {
        using var reader = new FieldReader(10, false);
        using var encoder = new QueryRowEncoder(reader);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => encoder.ReadAsync(4096, default));
        Assert.Equal("Full materialization forbidden.", error.Message);
        Assert.Equal(1, reader.ValueReads);
    }

    [Fact]
    public async Task OversizedText_StopsAtBudgetWithoutMaterializingValue()
    {
        using var reader = new GeneratedTextReader(1_000_000);
        await Assert.ThrowsAsync<ResponseBudgetExceededException>(() => BudgetedFieldReader.ReadTextAsync(reader, 4096, default));
        Assert.InRange(reader.UnitsRead, 4095, 4096);
    }

    [Fact]
    public void OversizedBinary_RejectsLengthBeforeReadingPayload()
    {
        using var reader = new FieldReader(1_000_000, true);
        Assert.Throws<ResponseBudgetExceededException>(() => BudgetedFieldReader.Read(reader, 0, 4096, default));
        Assert.Equal(0, reader.UnitsRead);
        Assert.Equal(0, reader.ValueReads);
    }

    [Fact]
    public void BinaryBudget_AccountsForBase64QuotesAndPadding()
    {
        foreach (var length in new[] { 0, 1, 2, 3, 4, 8193 })
        {
            using var reader = new FieldReader(length, true);
            var exact = ((long)length + 2) / 3 * 4 + 2;
            Assert.Throws<ResponseBudgetExceededException>(() => BudgetedFieldReader.Read(reader, 0, exact - 1, default));
            var data = Assert.IsType<byte[]>(BudgetedFieldReader.Read(reader, 0, exact, default));
            Assert.Equal(Enumerable.Repeat((byte)123, length), data);
            Assert.Equal(0, reader.ValueReads);
        }
    }

    [Fact]
    public async Task TextChunks_PreserveSurrogatePairsAndObserveCancellation()
    {
        var text = new string('a', 1023) + "😀汉\"\\\n";
        using var reader = new StringReader(text);
        Assert.Equal(text, await BudgetedFieldReader.ReadTextAsync(reader, 8192, default));
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new GeneratedTextReader(10000, cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BudgetedFieldReader.ReadTextAsync(cancelled, 8192, cancellation.Token));
        Assert.Equal(1024, cancelled.UnitsRead);
    }

    [Theory]
    [InlineData("SELECT hex(zeroblob(4194304)) AS value")]
    [InlineData("SELECT zeroblob(8388608) AS value")]
    public async Task SqliteLargeValue_TruncatesWholeRowAndConnectionRemainsUsable(string sql)
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        var result = await new BoundedQueryExecutor(2, 4096).QueryResultAsync(lease.Context, sql, [], default);
        Assert.Empty(result.Data);
        Assert.True(result.Truncated);
        using var reader = await lease.Client.Ado.GetDataReaderAsync(sql);
        Assert.True(await QueryReader.ReadAsync(reader, default));
        using var encoder = new QueryRowEncoder(reader);
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(4096, default));
        Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
        reader.Dispose();
        Assert.Equal(7, lease.Client.Ado.GetInt("SELECT 7"));
    }

    [Fact]
    public async Task FieldBudget_PreservesDynamicSqliteTypesAndRejectsDuplicateNames()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        var result = await new BoundedQueryExecutor(10, 8192).QueryResultAsync(lease.Context,
            "SELECT 1 AS v UNION ALL SELECT '汉😀' UNION ALL SELECT X'0080FF' UNION ALL SELECT NULL", [], default);
        var values = result.Data.Select(row => JsonSerializer.Deserialize<JsonElement>(row.Utf8Json).GetProperty("v")).ToArray();
        Assert.Equal(1, values[0].GetInt32());
        Assert.Equal("汉😀", values[1].GetString());
        Assert.Equal(new byte[] { 0, 128, 255 }, values[2].GetBytesFromBase64());
        Assert.Equal(JsonValueKind.Null, values[3].ValueKind);
        await Assert.ThrowsAsync<ArgumentException>(() => new BoundedQueryExecutor(10, 8192).QueryResultAsync(lease.Context,
            "SELECT 1 AS v,2 AS v", [], default));
        var multiple = await new BoundedQueryExecutor(10, 8192).MultipleAsync(lease.Client, "SELECT 1 AS v,2 AS v", []);
        using var json = JsonDocument.Parse(multiple.Utf8Json);
        var duplicateRow = json.RootElement.GetProperty("resultSets")[0].GetProperty("data")[0];
        Assert.Equal(1, duplicateRow.GetProperty("v").GetInt32());
        Assert.Equal(2, duplicateRow.GetProperty("v1").GetInt32());
    }

    private sealed class GeneratedTextReader(int length, Action? afterRead = null) : TextReader
    {
        public int UnitsRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copied = Math.Min(buffer.Length, length - UnitsRead);
            buffer.Span[..copied].Fill('x');
            UnitsRead += copied;
            afterRead?.Invoke();
            return ValueTask.FromResult(copied);
        }
    }

    private sealed class FieldReader(int length, bool binary) : DbDataReader
    {
        public int UnitsRead { get; private set; }
        public int ValueReads { get; private set; }
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int count) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int count)
        {
            if (buffer == null) return length;
            var copied = Math.Min(count, Math.Max(0, length - (int)offset));
            Array.Fill(buffer, (byte)123, bufferOffset, copied);
            UnitsRead += copied;
            return copied;
        }
        public override object GetValue(int ordinal) { ValueReads++; throw new InvalidOperationException("Full materialization forbidden."); }
        public override Type GetFieldType(int ordinal) => binary ? typeof(byte[]) : typeof(string);
        public override bool IsDBNull(int ordinal) => false;
        public override int FieldCount => 1;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(0);
        public override bool Read() => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override string GetName(int ordinal) => "value";
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override int GetOrdinal(string name) => 0;
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
    }
}
