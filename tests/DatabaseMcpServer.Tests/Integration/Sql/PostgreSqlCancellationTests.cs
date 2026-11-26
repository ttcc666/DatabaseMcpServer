using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class PostgreSqlCancellationTests(ITestOutputHelper output)
{
    [ProviderTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepeatedCancellation_StopsServerAndReusesSingleSlot(bool query)
    {
        var readerReturned = 0;
        using var fixture = new ProviderFixture("PostgreSQL", client =>
            client.Aop.OnLogExecuted = (_, _) => Interlocked.Exchange(ref readerReturned, 1));
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var marker = "dbmcp_" + Guid.NewGuid().ToString("N");
            Interlocked.Exchange(ref readerReturned, 0);
            var request = query
                ? fixture.StructuredQuery.QueryDatabase(fixture.SlowQuery(marker), cancellationToken: cancellation.Token).Json()
                : fixture.Command.ExecuteCommand(fixture.SlowCommand(marker), cancellationToken: cancellation.Token);
            Task? signal = null;
            var watch = new Stopwatch();
            try
            {
                await fixture.WaitForActivity(marker, true);
                if (iteration % 3 != 0) await Task.Delay(iteration % 3 * 25);
                Assert.False(request.IsCompleted);
                var readerReturnedAtCancellation = Volatile.Read(ref readerReturned) != 0;
                watch.Start();
                // The driver may perform synchronous network I/O in cancellation callbacks.
                // Include that time in the same assertion deadline instead of blocking before WaitAsync.
                signal = Task.Run(cancellation.Cancel);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(15)));
                var completedMs = watch.Elapsed.TotalMilliseconds;
                await signal.WaitAsync(TimeSpan.FromSeconds(5));
                var callbackCompletedMs = watch.Elapsed.TotalMilliseconds;
                await fixture.WaitForActivity(marker, false);
                var serverStoppedMs = watch.Elapsed.TotalMilliseconds;
                await fixture.AssertUsable();
                output.WriteLine(JsonSerializer.Serialize(new { query, iteration, readerReturnedAtCancellation,
                    completedMs, callbackCompletedMs, serverStoppedMs, reusedMs = watch.Elapsed.TotalMilliseconds }));
            }
            finally
            {
                signal ??= Task.Run(cancellation.Cancel);
                try { await request.WaitAsync(TimeSpan.FromSeconds(35)); }
                catch (OperationCanceledException) { }
                finally { await signal.WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
    }
}
