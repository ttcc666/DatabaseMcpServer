using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Configuration;
using ModelContextProtocol.Server;
using System.Threading.Channels;

namespace DatabaseMcpServer.Tools;

internal sealed class ToolExposurePolicy : IDisposable
{
    public static HashSet<string> SelectFor(IServiceProvider services, ToolCatalog catalog)
    {
        var profile = ParseProfile(Environment.GetEnvironmentVariable("DB_MCP_TOOL_PROFILE"));
        return Select(profile, catalog,
            (IDatabaseConfigService)services.GetService(typeof(IDatabaseConfigService))!,
            (DatabaseExecutionOptions)services.GetService(typeof(DatabaseExecutionOptions))!);
    }

    public static string ParseProfile(string? profile) => profile switch
    {
        null => "full",
        "full" or "analysis" => profile,
        _ => throw new ArgumentException("DB_MCP_TOOL_PROFILE 必须为 full 或 analysis。")
    };

    public string Profile { get; }
    private readonly ConfigurationChangeSignal? _changes;
    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _notifications;

    public ToolExposurePolicy(string? profile = null, ConfigurationChangeSignal? changes = null)
    {
        Profile = ParseProfile(profile);
        _changes = changes;
        if (_changes != null) _changes.Changed += OnChanged;
    }

    private void OnChanged() => _updates.Writer.TryWrite(true);

    public void Observe(McpServer server, ToolCatalog catalog, IDatabaseConfigService configuration, DatabaseExecutionOptions options)
    {
        lock (_gate)
        {
            if (_notifications != null) return;
            var previous = Select(catalog, configuration, options);
            _notifications = Task.Run(async () =>
            {
                try
                {
                    await foreach (var _ in _updates.Reader.ReadAllAsync(_stop.Token))
                    {
                        var current = Select(catalog, configuration, options);
                        if (previous.SetEquals(current)) continue;
                        previous = current;
                        await server.SendNotificationAsync("notifications/tools/list_changed", _stop.Token);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (ObjectDisposedException) { }
                catch (System.IO.IOException) { }
            });
        }
    }

    public void Dispose()
    {
        if (_changes != null) _changes.Changed -= OnChanged;
        _stop.Cancel();
        _updates.Writer.TryComplete();
    }

    public HashSet<string> Select(ToolCatalog catalog, IDatabaseConfigService configuration, DatabaseExecutionOptions options)
        => Select(Profile, catalog, configuration, options);

    private static HashSet<string> Select(string profile, ToolCatalog catalog, IDatabaseConfigService configuration, DatabaseExecutionOptions options)
    {
        var connections = configuration.GetAllConnections();
        return catalog.Tools.Where(t => (profile == "full" || t.Analysis) && t.RequiredAccess <= options.Access &&
            (t.Provider == "any" || connections.Any(c => t.RequiredAccess <= DatabaseAccessPolicy.Parse(c.AccessMode) &&
                (!t.Capability.HasValue || ProviderCapabilities.Supports(DatabaseProviderCatalog.Parse(c.DbType), t.Capability.Value)) &&
                (t.Provider == "mongo" ? string.Equals(c.DbType, "MongoDb", StringComparison.OrdinalIgnoreCase) :
                    !string.Equals(c.DbType, "MongoDb", StringComparison.OrdinalIgnoreCase)))))
            .Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
    }
}
