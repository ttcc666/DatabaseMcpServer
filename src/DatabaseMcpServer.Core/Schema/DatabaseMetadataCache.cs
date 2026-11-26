using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Services;

/// <summary>Bounded per-host metadata cache. SqlSugar's own schema cache is bypassed by callers.</summary>
internal sealed class DatabaseMetadataCache(DatabaseExecutionOptions options, TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private sealed record Key(string Database, long Version, string Table, string Sections);
    private sealed record Entry(DateTimeOffset Expires, Lazy<Task<JsonElement>> Value);

    public async Task<JsonElement> GetAsync(DatabaseOperationContext context, string table, bool refresh,
        Func<Task<JsonElement>> load, CancellationToken token, string sections = "")
    {
        token.ThrowIfCancellationRequested();
        if (options.MetadataCacheSeconds == 0) return await load();
        var key = new Key(context.ConnectionIdentity, context.ConfigurationVersion, table, sections);
        Entry entry;
        var owner = false;
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
                owner = true;
            }
        }
        (owner ? DatabaseTelemetry.CacheMisses : DatabaseTelemetry.CacheHits).Add(1);
        try
        {
            // The loader owns its connection. Never detach it on cancellation and release that lease early.
            var value = owner ? await entry.Value.Value : await entry.Value.Value.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            // Transient permission/provider errors must not hide a recovered section until TTL expires.
            if (System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > Math.Min(1_048_576, options.MaxResponseBytes) ||
                value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any(p =>
                    p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("status", out var status) && status.GetString() == "unavailable"))
            {
                lock (_gate)
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
            }
            return value;
        }
        catch (OperationCanceledException) when (!owner && !token.IsCancellationRequested)
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
            return await GetAsync(context, table, false, load, token, sections);
        }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry) &&
                    (owner || entry.Value.IsValueCreated && entry.Value.Value.IsCompleted)) _entries.Remove(key);
            throw;
        }
    }

    public void Invalidate(DatabaseOperationContext context)
    {
        var databaseName = context.ConnectionIdentity;
        lock (_gate)
            foreach (var key in _entries.Keys.Where(key => key.Database == databaseName).ToArray()) _entries.Remove(key);
    }
}
