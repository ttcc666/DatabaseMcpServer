using System.Text.Json;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

/// <summary>Bounded per-host metadata cache. SqlSugar's own schema cache is bypassed by callers.</summary>
internal sealed class DatabaseMetadataCache(DatabaseExecutionOptions options, TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private sealed record Key(string Database, long Version, string Table);
    private sealed record Entry(DateTimeOffset Expires, Lazy<JsonElement> Value);

    public JsonElement Get(DatabaseClientContext context, string table, bool refresh, Func<JsonElement> load)
    {
        if (options.MetadataCacheSeconds == 0) return load();
        var key = new Key(context.ConnectionIdentity, context.ConfigurationVersion, table);
        Entry entry;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (refresh || !_entries.TryGetValue(key, out entry!) || entry.Expires <= now)
            {
                foreach (var stale in _entries.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                    _entries.Remove(stale);
                if (_entries.Count >= options.MetadataCacheEntries)
                    _entries.Remove(_entries.MinBy(pair => pair.Value.Expires).Key);
                entry = new(now.AddSeconds(options.MetadataCacheSeconds), new(load, LazyThreadSafetyMode.ExecutionAndPublication));
                _entries[key] = entry;
            }
        }
        try { return entry.Value.Value; }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
            throw;
        }
    }

    public void Invalidate(DatabaseClientContext context)
    {
        var databaseName = context.ConnectionIdentity;
        lock (_gate)
            foreach (var key in _entries.Keys.Where(key => key.Database == databaseName).ToArray()) _entries.Remove(key);
    }
}
