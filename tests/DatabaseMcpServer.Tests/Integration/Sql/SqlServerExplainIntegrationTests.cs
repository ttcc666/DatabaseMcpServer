using System.Data;
using System.Data.Common;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class SqlServerExplainIntegrationTests
{
    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Showplan_CancellationAndCleanupFailureDoNotLeakSessionState(bool cleanupFailure)
    {
        using var cancellation = new CancellationTokenSource();
        var mode = new InterruptedPlanMode(cleanupFailure, cancellation);
        using var fixture = new ProviderFixture("SqlServer", configureServices: services => services.AddSingleton<SqlServerPlanMode>(mode));
        var call = fixture.Tool<ExplainTools>().ExplainQuery("SELECT 1", cancellationToken: cancellation.Token);
        if (cleanupFailure) Assert.True((await call).IsError);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(new[] { true, false }, mode.Transitions);
        Assert.True(mode.CleanupUsedIndependentToken);
        Assert.Equal(ConnectionState.Closed, mode.Connection!.State);
        // If SHOWPLAN leaked into the pool, this would return XML instead of 17.
        await fixture.AssertUsable();
    }

    private sealed class InterruptedPlanMode(bool cleanupFailure, CancellationTokenSource cancellation) : SqlServerPlanMode
    {
        public List<bool> Transitions { get; } = [];
        public DbConnection? Connection { get; private set; }
        public bool CleanupUsedIndependentToken { get; private set; }

        public override async Task SetAsync(DbConnection connection, bool enabled, int timeout, CancellationToken token)
        {
            Transitions.Add(enabled);
            if (Connection != null) Assert.Same(Connection, connection);
            Connection = connection;
            if (!enabled)
            {
                CleanupUsedIndependentToken = !token.IsCancellationRequested;
                if (cleanupFailure) throw new IOException("Simulated failure before SHOWPLAN OFF");
            }
            await base.SetAsync(connection, enabled, timeout, token);
            if (enabled && !cleanupFailure) cancellation.Cancel();
        }
    }
}
