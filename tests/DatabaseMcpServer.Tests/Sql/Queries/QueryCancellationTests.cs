using System.Text.Json;
using System.Collections;
using System.Data.Common;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Web;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class QueryCancellationTests
{
    [Fact]
    public async Task StructuredQueryCancellation_RecordsCompletionAfterLeaseRelease()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new RuntimeConsistencyTests.Fixture(client => client.Aop.OnLogExecuting = (_, _) => cancellation.Cancel());
        var durations = new List<double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == DatabaseTelemetry.MeterName && instrument.Name == "dbmcp.cancellation.completion")
                meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
        listener.Start();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Host.Services.GetRequiredService<DatabaseQueryTools>()
            .QueryDatabase("SELECT 1", cancellationToken: cancellation.Token));
        Assert.InRange(Assert.Single(durations), 0, 15000);
        using var lease = fixture.Sessions.AcquireClient();
        Assert.Equal(17, lease.Client.Ado.GetInt("SELECT 17"));
    }

    [Fact]
    public async Task PreCancelledQuery_DoesNotExecuteAndTokenIsNotAnOption()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var tool = fixture.Host.Services.GetRequiredService<DatabaseQueryTools>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.QueryDatabase("select * from missing", cancellationToken: cancellation.Token));
        var catalog = new ToolCatalog();
        var metadata = Assert.Single(catalog.Tools, t => t.Name == "query_database");
        Assert.DoesNotContain(metadata.Parameters, p => p.ParameterType == typeof(CancellationToken));
        var args = ToolInvoker.BindArguments(metadata, new Dictionary<string, string?> { ["sql"] = "select 1" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolInvoker.InvokeAsync(metadata, tool, args, cancellation.Token));
        var web = fixture.Host.Services.GetRequiredService<CliWebToolService>();
        var arguments = new Dictionary<string, JsonElement> { ["sql"] = JsonSerializer.SerializeToElement("select 1") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => web.InvokeAsync("query_database", new(arguments, null), cancellation.Token));
        var runner = new CliRunner(catalog, new(), new CliWebHost(), fixture.StatePath, false);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(130, await runner.RunAsync(["tool", "query_database", "--sql", "select 1", "--config", fixture.Path], stdout, stderr, cancellation.Token));
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task CancellationDuringBatch_StopsFollowingSqlAndDiscardsLease()
    {
        using var cancellation = new CancellationTokenSource();
        var executed = new List<string>();
        SqlSugar.ISqlSugarClient? client = null;
        using var fixture = new RuntimeConsistencyTests.Fixture(acquired =>
        {
            if (client != null) return;
            client = acquired;
            acquired.Aop.OnLogExecuting = (sql, _) => { executed.Add(sql); cancellation.Cancel(); };
        });
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        using var queries = JsonDocument.Parse("""["select 1", "select 2"]""");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!.Select(sql => new DatabaseMcpServer.Models.DatabaseCommand { Sql = sql }).ToArray(), cancellationToken: cancellation.Token));
        Assert.Single(executed);
        Assert.DoesNotContain("select 2", executed);
        using var next = fixture.Sessions.AcquireClient();
        Assert.NotSame(client, next.Client);
        Assert.Equal(3, next.Client.Ado.GetInt("select 3"));
    }

    [Fact]
    public async Task CommandScope_RestoresTimeoutAndCancellationOnFailure()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        using var lease = fixture.Sessions.AcquireClient();
        var originalTimeout = lease.Client.Ado.CommandTimeOut;
        var originalToken = lease.Client.Ado.CancellationToken;
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlCommandTimeout.WithTimeoutAsync<int>(lease.Client, 1, cancel.Token, client =>
        {
            Assert.Equal(1, client.Ado.CommandTimeOut);
            Assert.Equal(cancel.Token, client.Ado.CancellationToken);
            throw new InvalidOperationException("expected");
        }));
        Assert.Equal(originalTimeout, lease.Client.Ado.CommandTimeOut);
        Assert.Equal(originalToken, lease.Client.Ado.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderCancellation_HoldsLeaseUntilDriverExits(bool nextResult)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var leases = Enumerable.Range(0, 7).Select(_ => fixture.Sessions.AcquireClient()).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancel = new CancellationTokenSource();
        using var reader = new ControlledReader();
        var probe = new ReaderProbe(fixture.Host.Services);
        try
        {
            var request = probe.Run(reader, nextResult, cancel.Token);
            await reader.Entered.Task.WaitAsync(timeout.Token);
            var queued = fixture.Sessions.AcquireClientAsync(timeout.Token).AsTask();
            Assert.False(queued.IsCompleted);
            cancel.Cancel();
            await reader.Cancelled.Task.WaitAsync(timeout.Token);
            Assert.False(request.IsCompleted);
            Assert.False(queued.IsCompleted);
            Assert.False(reader.Closed);
            reader.Exit.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.True(reader.Closed);
            using var next = await queued;
            Assert.Equal(17, next.Client.Ado.GetInt("select 17"));
        }
        finally
        {
            reader.Exit.TrySetResult();
            foreach (var lease in leases) lease.Dispose();
        }
    }

    private sealed class ReaderProbe(IServiceProvider services) : DatabaseMcpServer.Services.SqlOperationExecutor(
        services.GetRequiredService<IDatabaseConfigService>(), services.GetRequiredService<IDatabaseHelperService>(),
        NullLogger.Instance,
        services.GetRequiredService<DatabaseMcpServer.Models.DatabaseExecutionOptions>(), services.GetRequiredService<DatabaseMcpServer.Services.DatabaseMetadataCache>(),
        services.GetRequiredService<DatabaseMcpServer.Services.SqlDatabaseSessions>())
    {
        public Task<object> Run(ControlledReader reader, bool nextResult, CancellationToken token) => WithDeadlineAsync(async (_, requestToken) =>
        {
            using (reader)
                return nextResult ? await QueryReader.NextResultAsync(reader, requestToken) : await QueryReader.ReadAsync(reader, requestToken);
        }, null, token);
    }

    // Models a driver which observes cancellation immediately but needs cleanup before it returns.
    private sealed class ControlledReader : DbDataReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            await Exit.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => ReadAsync(cancellationToken);
        public override void Close() => Closed = true;
        protected override void Dispose(bool disposing) { if (disposing) Close(); base.Dispose(disposing); }
        public override int Depth => 0;
        public override int FieldCount => 0;
        public override bool HasRows => false;
        public override bool IsClosed => Closed;
        public override int RecordsAffected => 0;
        public override object this[int ordinal] => throw new NotSupportedException();
        public override object this[string name] => throw new NotSupportedException();
        public override bool Read() => throw new NotSupportedException();
        public override bool NextResult() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetName(int ordinal) => throw new NotSupportedException();
        public override int GetOrdinal(string name) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override object GetValue(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
