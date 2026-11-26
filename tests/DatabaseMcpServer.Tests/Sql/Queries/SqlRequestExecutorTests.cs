using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tests;

public class SqlRequestExecutorTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("command")]
    [InlineData("batch")]
    [InlineData("queryBatch")]
    [InlineData("multiple")]
    [InlineData("explain")]
    [InlineData("page")]
    public async Task EntryPoints_IncludeQueueInDeadlineAndRecordOnce(string entry)
    {
        using var fixture = DatabaseRoleTests.Create(new(MaxConcurrent: 1));
        using var metrics = new Metrics();
        using (var held = fixture.Sessions.AcquireClient())
        {
            var result = await Invoke(fixture.Host.Services, entry).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Equal(1011, result.GetProperty("errorCode").GetInt32());
            Assert.False(result.GetProperty("retryable").GetBoolean());
            if (entry == "batch")
            {
                var progress = result.GetProperty("data");
                Assert.Equal(1, progress.GetProperty("totalCommands").GetInt32());
                Assert.Empty(progress.GetProperty("results").EnumerateArray());
                Assert.Equal(JsonValueKind.Null, progress.GetProperty("unknownCommandIndex").ValueKind);
                Assert.Equal(0, progress.GetProperty("notExecutedFromIndex").GetInt32());
            }
        }
        Assert.Equal(1, metrics.Count("dbmcp.request.duration"));
        Assert.Equal(1, metrics.Count("dbmcp.cancellation.completion"));
        Assert.Equal(1, metrics.Count("dbmcp.cancelled"));
        using var next = fixture.Sessions.AcquireClient();
        Assert.Equal(1, next.Client.Ado.GetInt("SELECT count(*) FROM markers"));
    }

    private static async Task<JsonElement> Invoke(IServiceProvider services, string entry)
    {
        var queries = services.GetRequiredService<QueryTools>();
        var commands = services.GetRequiredService<CommandTools>();
        object result = entry switch
        {
            "query" => await services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("SELECT 1", commandTimeoutSeconds: 1),
            "command" => await commands.ExecuteCommand("INSERT INTO markers VALUES(99)", commandTimeoutSeconds: 1),
            "batch" => await commands.BatchExecuteCommands([new() { Sql = "INSERT INTO markers VALUES(99)" }], commandTimeoutSeconds: 1),
            "queryBatch" => await queries.BatchSqlQuery([new() { Sql = "SELECT 1" }], commandTimeoutSeconds: 1),
            "multiple" => await queries.GetDataSetAll("SELECT 1; SELECT 2", commandTimeoutSeconds: 1),
            "page" => await queries.QueryTablePage("markers", commandTimeoutSeconds: 1),
            "explain" => await services.GetRequiredService<SqlExplainService>().Explain("SELECT 1", null, null, null, 1, default),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
        return result switch
        {
            CallToolResult tool => tool.StructuredContent!.Value,
            string json => JsonSerializer.Deserialize<JsonElement>(json),
            _ => JsonSerializer.SerializeToElement(result, JsonSerializationDefaults.CompactCamelCase)
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_WaitsForCleanupAndUserCancellationWins(bool userCancels)
    {
        using var fixture = DatabaseRoleTests.Create(new(MaxConcurrent: 1));
        var executor = fixture.Host.Services.GetRequiredService<SqlRequestExecutor>();
        using var user = new CancellationTokenSource();
        using var metrics = new Metrics();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var request = executor.ExecuteAsync<object>(async (context, token) =>
        {
            using var registration = token.Register(() => observed.TrySetResult());
            try { await release.Task; throw new InvalidOperationException("driver cancellation wrapper"); }
            finally { cleaned = true; }
        }, DatabaseToolAccess.Read, 1, user.Token, null, NullLogger.Instance, error => error);
        var queued = fixture.Sessions.AcquireClientAsync().AsTask();
        try
        {
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(request.IsCompleted);
            Assert.False(queued.IsCompleted);
            Assert.False(cleaned);
            Assert.Equal(0, metrics.Count("dbmcp.cancellation.completion"));
            if (userCancels) user.Cancel();
            release.TrySetResult();
            if (userCancels)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
                Assert.Equal(user.Token, error.CancellationToken);
            }
            else
                Assert.Equal(DatabaseErrorCode.CommandTimeout, Assert.IsType<ApiResult<object>>(await request).ErrorCode);
            Assert.True(cleaned);
            Assert.Equal(1, metrics.Count("dbmcp.cancelled"));
            Assert.Equal(userCancels ? 0 : 1, metrics.Count("dbmcp.request.timeouts"));
            Assert.Equal(1, metrics.Count("dbmcp.request.duration"));
            Assert.Equal(1, metrics.Count("dbmcp.cancellation.completion"));
        }
        finally
        {
            release.TrySetResult();
            using var next = await queued.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(17, next.Client.Ado.GetInt("SELECT 17"));
        }
    }

    [Fact]
    public async Task QueuePolicyFailure_RemainsRetryableAdmissionError()
    {
        using var fixture = DatabaseRoleTests.Create(new(MaxConcurrent: 1, QueueTimeoutSeconds: 1));
        using var held = fixture.Sessions.AcquireClient();
        var response = await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("SELECT 1", commandTimeoutSeconds: 10);
        Assert.True(response.IsError);
        var result = response.StructuredContent!.Value;
        Assert.Equal(1009, result.GetProperty("errorCode").GetInt32());
        Assert.True(result.GetProperty("retryable").GetBoolean());
        Assert.Equal("admission", result.GetProperty("phase").GetString());
    }

    [Theory]
    [InlineData(null, true, 2)]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 1)]
    [InlineData(0, false, -1)]
    [InlineData(-1, true, -1)]
    [InlineData(3, true, -1)]
    public async Task TimeoutPolicy_AppliesDefaultCeilingAndExplicitValues(int? timeout, bool infinite, int expected)
    {
        using var fixture = DatabaseRoleTests.Create(new() { DefaultCommandTimeoutSeconds = 5, MaxCommandTimeoutSeconds = 2, AllowInfiniteTimeout = infinite });
        var calls = 0;
        var result = await fixture.Host.Services.GetRequiredService<SqlRequestExecutor>().ExecuteAsync<object>((context, token) =>
        {
            calls++;
            Assert.True(token.CanBeCanceled);
            return Task.FromResult<object>(context.Client.Ado.CommandTimeOut);
        }, DatabaseToolAccess.Read, timeout, default, null, NullLogger.Instance, error => error);
        if (expected < 0)
        {
            Assert.Equal(0, calls);
            Assert.Equal(DatabaseErrorCode.InvalidParameters, Assert.IsType<ApiResult<object>>(result).ErrorCode);
        }
        else
        {
            Assert.Equal(1, calls);
            Assert.Equal(expected == 0 ? 0 : expected + 1, Assert.IsType<int>(result));
        }
    }

    [Fact]
    public async Task MaximumDeadline_PreservesLaterDriverFallback()
    {
        using var fixture = DatabaseRoleTests.Create();
        var result = await fixture.Host.Services.GetRequiredService<SqlRequestExecutor>().ExecuteAsync<object>((context, _) =>
            Task.FromResult<object>(context.Client.Ado.CommandTimeOut), DatabaseToolAccess.Read, 86400, default, null, NullLogger.Instance, error => error);
        Assert.Equal(86401, Assert.IsType<int>(result));
    }

    [Fact]
    public async Task Admission_DoesNotRestartExecutionDeadline()
    {
        using var fixture = DatabaseRoleTests.Create(new(MaxConcurrent: 1));
        using var held = fixture.Sessions.AcquireClient();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = fixture.Host.Services.GetRequiredService<SqlRequestExecutor>();
        var request = executor.ExecuteAsync<object>(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, DatabaseToolAccess.Read, 3, default, null, NullLogger.Instance, error => error);
        Assert.False(request.IsCompleted);
        await Task.Delay(1800);
        held.Dispose();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        // A restarted three-second timer would exceed this bound; the original deadline has ~1.2 s left.
        var result = await request.WaitAsync(TimeSpan.FromSeconds(2.5));
        Assert.Equal(DatabaseErrorCode.CommandTimeout, Assert.IsType<ApiResult<object>>(result).ErrorCode);
    }

    [Fact]
    public async Task SuccessfulRequest_RecordsOnceWithoutCancellation()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var metrics = new Metrics();
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("SELECT 17 AS value"));
        Assert.Equal(17, result.GetProperty("data")[0].GetProperty("value").GetInt32());
        Assert.Equal(1, metrics.Count("dbmcp.request.duration"));
        Assert.Equal(0, metrics.Count("dbmcp.cancelled"));
        Assert.Equal(0, metrics.Count("dbmcp.cancellation.completion"));
    }

    private sealed class Metrics : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, int> _counts = new();
        public Metrics()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DatabaseTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => _counts.AddOrUpdate(instrument.Name, 1, (_, count) => count + 1));
            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => _counts.AddOrUpdate(instrument.Name, (int)value, (_, count) => count + (int)value));
            _listener.Start();
        }
        public int Count(string name) => _counts.GetValueOrDefault(name);
        public void Dispose() => _listener.Dispose();
    }
}
