using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.IO.Pipelines;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog.Core;
using Serilog.Events;

namespace DatabaseMcpServer.Tests;

public class ExecutionObservabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolTrace_CorrelatesPhasesAndLogsWithoutPayloadTags(bool web)
    {
        using var observations = new Observations();
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var catalog = new ToolCatalog();
        Assert.True(catalog.TryGetTool("query_database", out var descriptor));
        var arguments = ToolInvoker.BindJsonArguments(descriptor, new Dictionary<string, JsonElement>
        { ["sql"] = JsonSerializer.SerializeToElement("SELECT 'secret-value' AS value") });
        if (web)
        {
            var response = await fixture.Host.Services.GetRequiredService<CliWebToolService>().InvokeAsync("query_database",
                new(new Dictionary<string, JsonElement> { ["sql"] = JsonSerializer.SerializeToElement("SELECT 'secret-value' AS value") }, null), default);
            Assert.Equal("secret-value", JsonSerializer.SerializeToElement(response).GetProperty("result").GetProperty("data")[0].GetProperty("value").GetString());
        }
        else
        {
            var payload = await ToolInvoker.InvokeAsync(descriptor, fixture.Host.Services.GetRequiredService<DatabaseQueryTools>(), arguments);
            Assert.Equal("secret-value", DatabaseRoleTests.Success(payload).GetProperty("data")[0].GetProperty("value").GetString());
        }
        var tool = Assert.Single(observations.Activities, a => a.OperationName == "dbmcp.tool");
        Assert.Equal(ActivityIdFormat.W3C, tool.IdFormat);
        foreach (var name in new[] { "dbmcp.request.duration", "dbmcp.admission", "dbmcp.read.duration" })
        {
            var phase = Assert.Single(observations.Activities, a => a.OperationName == name);
            Assert.Equal(tool.TraceId, phase.TraceId);
            Assert.NotEqual(default, phase.ParentSpanId);
        }
        Assert.All(observations.Activities, activity => { Assert.Empty(activity.Tags); Assert.Empty(activity.Events); });
        Assert.Null(Activity.Current);

        var sink = new LogSink();
        using var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using (var scope = DatabaseTelemetry.StartTool())
        {
            logger.Information("Correlation test");
            Assert.Equal(scope.TraceId, Assert.Single(sink.Events).TraceId);
        }
    }

    [Fact]
    public async Task McpTrace_CoversInvocationAndInvalidArgumentsWithoutChangingPayload()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services => services.AddDatabaseMcpServer());
        using var observations = new Observations();
        var outgoing = new Pipe(); var incoming = new Pipe();
        var options = fixture.Host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        await using var server = McpServer.Create(new StreamServerTransport(outgoing.Reader.AsStream(), incoming.Writer.AsStream()),
            options, serviceProvider: fixture.Host.Services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var running = server.RunAsync(timeout.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(outgoing.Writer.AsStream(), incoming.Reader.AsStream()),
                cancellationToken: timeout.Token);
            var valid = await client.CallToolAsync("query_database", new Dictionary<string, object?> { ["sql"] = "select 7 as value" }, cancellationToken: timeout.Token);
            Assert.False(valid.IsError == true);
            Assert.Equal(7, valid.StructuredContent!.Value.GetProperty("data")[0].GetProperty("value").GetInt32());
            var invalid = await client.CallToolAsync("query_database", new Dictionary<string, object?> { ["sql"] = 7 }, cancellationToken: timeout.Token);
            Assert.True(invalid.IsError);
            Assert.Equal(1004, invalid.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            var tools = observations.Activities.Where(a => a.OperationName == "dbmcp.tool").ToArray();
            Assert.Equal(2, tools.Length);
            Assert.NotEqual(tools[0].SpanId, tools[1].SpanId);
            Assert.Equal(tools[0].TraceId, Assert.Single(observations.Activities, a => a.OperationName == "dbmcp.read.duration").TraceId);
            Assert.All(observations.Activities, activity => Assert.Empty(activity.Tags));
        }
        finally { timeout.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task ParallelToolScopes_RestoreParentAndKeepDistinctRequests()
    {
        using var parent = new Activity("parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ActivitySpanId> Run()
        {
            using var tool = DatabaseTelemetry.StartTool();
            Assert.Equal(parent.TraceId, tool.TraceId);
            Assert.Equal(parent.SpanId, tool.ParentSpanId);
            await hold.Task;
            Assert.Same(tool, Activity.Current);
            return tool.SpanId;
        }
        var first = Run(); var second = Run();
        Assert.Same(parent, Activity.Current);
        hold.SetResult();
        Assert.NotEqual(await first, await second);
        Assert.Same(parent, Activity.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MongoCancellation_RecordsCompletionOnlyAfterCleanup(bool deadline)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = MongoCollectionServiceTests.Fixture(configure: services =>
            ObservedClientFactory.Register(services, db =>
            {
                db.CurrentConnectionConfig.SqlMiddle = new SqlSugar.SqlMiddle
                {
                    IsSqlMiddle = true,
                    GetDataReaderAsync = async (_, _) =>
                    {
                        entered.TrySetResult();
                        try { await Task.Delay(Timeout.Infinite, db.Ado.CancellationToken ?? default); }
                        finally { cancelled.TrySetResult(); await cleanup.Task; }
                        throw new InvalidOperationException();
                    }
                };
            }));
        using var observations = new Observations();
        using var cancellation = new CancellationTokenSource();
        var request = fixture.Host.Services.GetRequiredService<MongoCollectionService>().ExecuteAsync(
            new(MongoOperation.Find, "docs", CommandTimeoutSeconds: deadline ? 1 : 30), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!deadline) cancellation.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain("dbmcp.cancellation.completion", observations.Durations);
            cleanup.SetResult();
            if (deadline) Assert.Equal(1011, (await request).ErrorCode);
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.Single(observations.Durations, d => d == "dbmcp.cancellation.completion");
            Assert.Equal(1, observations.Cancelled);
            Assert.Equal(deadline ? 1 : 0, observations.Timeouts);
        }
        finally { cleanup.TrySetResult(); cancellation.Cancel(); }
    }

    private sealed class LogSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class Observations : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly MeterListener _metrics = new();
        public ConcurrentQueue<Activity> Activities { get; } = new();
        public ConcurrentQueue<string> Durations { get; } = new();
        private long _cancelled;
        private long _timeouts;
        public long Cancelled => Interlocked.Read(ref _cancelled);
        public long Timeouts => Interlocked.Read(ref _timeouts);
        public Observations()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DatabaseTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => Activities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_activities);
            _metrics.InstrumentPublished = (instrument, listener) =>
            { if (instrument.Meter.Name == DatabaseTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
            _metrics.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            { Assert.Empty(tags.ToArray()); Assert.True(value >= 0); Durations.Enqueue(instrument.Name); });
            _metrics.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name != "dbmcp.rejected") Assert.Empty(tags.ToArray());
                if (instrument.Name == "dbmcp.cancelled") Interlocked.Add(ref _cancelled, value);
                if (instrument.Name == "dbmcp.request.timeouts") Interlocked.Add(ref _timeouts, value);
            });
            _metrics.Start();
        }
        public void Dispose() { _metrics.Dispose(); _activities.Dispose(); }
    }
}
