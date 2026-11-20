using System.Reflection;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using MongoDB.Driver;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

public class MongoClientRegistryTests
{
    internal static DatabaseClientContext Context(string uri, long version = 1, string identity = "one") =>
        new(new SqlSugarClient(new ConnectionConfig { DbType = SqlSugar.DbType.MongoDb, ConnectionString = uri }), false, identity)
        { ConfigurationVersion = version, DatabaseName = "mongo", DatabaseType = "MongoDb" };

    [Fact]
    public async Task ClientRegistry_PreservesCaseSensitiveCredentialsAndRetiresInflightClients()
    {
        var created = new List<ClientTracker>();
        using var registry = new MongoClientRegistry((uri, _) =>
        {
            var tracker = ClientTracker.Create(uri);
            created.Add(tracker);
            return Task.FromResult(tracker.Proxy);
        });
        var first = await registry.AcquireAsync(Context("mongodb://User:Secret@localhost:1/App"), default);
        var reuse = await registry.AcquireAsync(Context("mongodb://User:Secret@localhost:1/App"), default);
        Assert.Single(created);
        var next = await registry.AcquireAsync(Context("mongodb://User:secret@localhost:1/App", 2), default);
        Assert.Equal(2, created.Count);
        Assert.Equal(0, created[0].Disposed);
        first.Dispose();
        Assert.Equal(0, created[0].Disposed);
        reuse.Dispose();
        Assert.Equal(1, created[0].Disposed);
        using (var queuedOld = await registry.AcquireAsync(Context("mongodb://User:Secret@localhost:1/App", 1), default))
        {
            Assert.Equal("App", queuedOld.Database.DatabaseNamespace.DatabaseName);
            Assert.Equal(3, created.Count);
            Assert.Equal(0, created[1].Disposed);
        }
        Assert.Equal(1, created[2].Disposed);
        next.Dispose();
        registry.Dispose();
        Assert.All(created, c => Assert.Equal(1, c.Disposed));
    }

    [Fact]
    public async Task ClientRegistry_BoundsEntriesAndNeverDisposesActiveClients()
    {
        var created = new List<ClientTracker>();
        using var registry = new MongoClientRegistry((uri, _) =>
        { var item = ClientTracker.Create(uri); created.Add(item); return Task.FromResult(item.Proxy); }, 2);
        using var first = await registry.AcquireAsync(Context("mongodb://localhost:1/a", identity: "a"), default);
        var idle = await registry.AcquireAsync(Context("mongodb://localhost:1/b", identity: "b"), default);
        idle.Dispose();
        using var third = await registry.AcquireAsync(Context("mongodb://localhost:1/c", identity: "c"), default);
        Assert.Equal(1, created[1].Disposed);
        Assert.Equal(0, created[0].Disposed);
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => registry.AcquireAsync(Context("mongodb://localhost:1/d", identity: "d"), default));
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, error.ErrorCode);
        registry.Dispose();
        Assert.Equal(0, created[0].Disposed);
        first.Dispose(); third.Dispose();
        Assert.All(created, c => Assert.Equal(1, c.Disposed));
    }

    [Fact]
    public async Task ReloadBackToOriginalUri_RevivesClientAndRetiresNewerUri()
    {
        var created = new List<ClientTracker>();
        using var registry = new MongoClientRegistry((uri, _) =>
        { var item = ClientTracker.Create(uri); created.Add(item); return Task.FromResult(item.Proxy); });
        var original = await registry.AcquireAsync(Context("mongodb://localhost:1/a", 1), default);
        var replacement = await registry.AcquireAsync(Context("mongodb://localhost:1/b", 2), default);
        var restored = await registry.AcquireAsync(Context("mongodb://localhost:1/a", 3), default);
        original.Dispose(); restored.Dispose();
        Assert.Equal(0, created[0].Disposed);
        replacement.Dispose();
        Assert.Equal(1, created[1].Disposed);
        using var latest = await registry.AcquireAsync(Context("mongodb://localhost:1/a", 3), default);
        Assert.Equal(2, created.Count);
        Assert.Equal("a", latest.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public async Task ConcurrentInitialization_IsSharedAndCancelledFailureDoesNotPoisonRegistry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creations = 0;
        using var registry = new MongoClientRegistry(async (uri, token) =>
        {
            Interlocked.Increment(ref creations);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new MongoClient(uri);
        });
        var context = Context("mongodb://localhost:1/test");
        using var cancellation = new CancellationTokenSource();
        var first = registry.AcquireAsync(context, cancellation.Token);
        await entered.Task;
        var second = registry.AcquireAsync(context, default);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        using var ready = await second.WaitAsync(TimeSpan.FromSeconds(5));
        using var reused = await registry.AcquireAsync(context, default);
        Assert.Equal(2, creations);
        Assert.Equal("test", ready.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void SrvValidation_IsOfflineAndRequiresExplicitDatabase()
    {
        Assert.Equal("App", MongoClientRegistry.ValidateUri("mongodb+srv://User:Secret@nonexistent.invalid/App?authSource=admin").DatabaseName);
        Assert.Throws<DatabaseMcpException>(() => MongoClientRegistry.ValidateUri("mongodb://localhost/?authSource=admin"));
        var settings = MongoClientRegistry.SettingsFromResolvedUrl(new MongoUrl("mongodb+srv://User:Secret@nonexistent.invalid/App?authSource=admin&srvMaxHosts=2&srvServiceName=custom&retryWrites=false"));
        Assert.Equal(MongoDB.Driver.Core.Configuration.ConnectionStringScheme.MongoDBPlusSrv, settings.Scheme);
        Assert.Equal("nonexistent.invalid", Assert.Single(settings.Servers).Host);
        Assert.Equal("admin", settings.Credential.Source);
        Assert.Equal("User", settings.Credential.Username);
        Assert.True(settings.UseTls);
        Assert.False(settings.RetryWrites);
        Assert.Equal(2, settings.SrvMaxHosts);
        Assert.Equal("custom", settings.SrvServiceName);
        Assert.False(MongoClientRegistry.SettingsFromResolvedUrl(new MongoUrl("mongodb+srv://nonexistent.invalid/App?tls=false")).UseTls);
    }

    public class ClientTracker : DispatchProxy
    {
        private MongoClient _actual = null!;
        public IMongoClient Proxy { get; private set; } = null!;
        public int Disposed { get; private set; }
        public static ClientTracker Create(string uri)
        {
            var proxy = Create<IMongoClient, ClientTracker>();
            var tracker = (ClientTracker)proxy;
            tracker.Proxy = proxy;
            tracker._actual = new MongoClient(uri);
            return tracker;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "Dispose") Disposed++;
            return targetMethod.Invoke(_actual, args);
        }
    }
}
