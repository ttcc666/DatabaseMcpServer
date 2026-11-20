using System.Security.Cryptography;
using System.Text;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;

namespace DatabaseMcpServer.Services;

internal sealed class MongoClientRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, Task<IMongoClient>> _factory;
    private readonly int _capacity;
    private long _clock;
    private bool _disposed;

    public MongoClientRegistry() : this(CreateClientAsync, 128) { }
    internal MongoClientRegistry(Func<string, CancellationToken, Task<IMongoClient>> factory, int capacity = 128)
    { _factory = factory; _capacity = capacity; }

    internal static MongoUrlBuilder ValidateUri(string uri)
    {
        try
        {
            var builder = new MongoUrlBuilder(uri);
            if (string.IsNullOrWhiteSpace(builder.DatabaseName)) throw MongoDocumentContract.Invalid("Mongo URI 必须显式指定数据库；authSource 不能替代数据库名。");
            return builder;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw MongoDocumentContract.Invalid("Mongo URI 无效，请检查官方驱动连接选项。"); }
    }

    private static async Task<IMongoClient> CreateClientAsync(string uri, CancellationToken token)
    {
        var url = new MongoUrl(uri);
        // Use the explicit overload: Driver 3.11.1's token-only overload drops its token.
        var resolved = await url.ResolveAsync(url.DirectConnection, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new MongoClient(SettingsFromResolvedUrl(resolved));
    }

    internal static MongoClientSettings SettingsFromResolvedUrl(MongoUrl resolved)
    {
        if (resolved.Scheme != ConnectionStringScheme.MongoDBPlusSrv) return MongoClientSettings.FromUrl(resolved);
        // ResolveAsync(false) merges TXT options but leaves IsResolved=false in MongoUrl 3.11.1.
        // Convert the already resolved options offline, then restore SRV monitoring settings.
        // This avoids FromUrl's second, synchronous TXT lookup without disabling SRV polling.
        var offline = new MongoUrlBuilder(resolved.ToString())
        { Scheme = ConnectionStringScheme.MongoDB, SrvMaxHosts = null, SrvServiceName = "mongodb" };
        var settings = MongoClientSettings.FromUrl(offline.ToMongoUrl());
        settings.Scheme = resolved.Scheme;
        settings.SrvMaxHosts = resolved.SrvMaxHosts ?? 0;
        settings.SrvServiceName = resolved.SrvServiceName;
        return settings;
    }

    public async Task<Lease> AcquireAsync(DatabaseClientContext context, CancellationToken token)
    {
        var uri = context.Client.CurrentConnectionConfig.ConnectionString;
        var database = ValidateUri(uri).DatabaseName;
        var key = context.ConnectionIdentity + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var old in _entries.Values.Where(e => e.Key != key && e.Identity == context.ConnectionIdentity && e.Version < context.ConfigurationVersion).ToArray())
            { old.Retired = true; if (old.References == 0) Remove(old); }
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (_entries.Count >= _capacity)
                {
                    var idle = _entries.Values.Where(e => e.References == 0).MinBy(e => e.LastUsed);
                    if (idle == null) throw new DatabaseMcpException(DatabaseErrorCode.DatabaseBusy, "Mongo 客户端容量已满，请稍后重试。");
                    Remove(idle);
                }
                entry = new Entry(key, context.ConnectionIdentity, context.ConfigurationVersion)
                { Retired = _entries.Values.Any(e => e.Identity == context.ConnectionIdentity && e.Version > context.ConfigurationVersion) };
                _entries.Add(key, entry);
            }
            else if (context.ConfigurationVersion > entry.Version)
            {
                entry.Version = context.ConfigurationVersion;
                entry.Retired = _entries.Values.Any(e => e.Identity == context.ConnectionIdentity && e.Version > entry.Version);
            }
            entry.References++;
            entry.LastUsed = ++_clock;
        }
        try
        {
            await entry.Initialization.WaitAsync(token).ConfigureAwait(false);
            try { entry.Client ??= await _factory(uri, token).ConfigureAwait(false); }
            finally { entry.Initialization.Release(); }
            token.ThrowIfCancellationRequested();
            return new(entry.Client.GetDatabase(database), () => Release(entry));
        }
        catch { Release(entry); throw; }
    }

    private void Release(Entry entry)
    {
        lock (_gate)
        {
            entry.References--;
            entry.LastUsed = ++_clock;
            if (entry.References == 0 && (entry.Retired || entry.Client == null || _disposed)) Remove(entry);
        }
    }

    private void Remove(Entry entry)
    {
        _entries.Remove(entry.Key);
        entry.Client?.Dispose();
        entry.Initialization.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _entries.Values.Where(e => e.References == 0).ToArray()) Remove(entry);
        }
    }

    private sealed class Entry(string key, string identity, long version)
    {
        public string Key { get; } = key;
        public string Identity { get; } = identity;
        public long Version { get; set; } = version;
        public IMongoClient? Client;
        public SemaphoreSlim Initialization { get; } = new(1, 1);
        public int References;
        public long LastUsed;
        public bool Retired;
    }

    internal sealed class Lease(IMongoDatabase database, Action release) : IDisposable
    {
        private Action? _release = release;
        public IMongoDatabase Database { get; } = database;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
