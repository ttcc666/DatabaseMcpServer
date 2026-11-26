using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Prompts;
using DatabaseMcpServer.Resources;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseMcpApplicationServices(
        this IServiceCollection services,
        bool cliToolMode = false,
        string? currentDatabaseStateFilePath = null,
        bool? enableMonitorConfig = null,
        string? configPath = null,
        bool allowUnconfigured = false)
    {
        var resolvedPath = ConfigurationPathResolver.ResolveWritablePath(
            configPath ?? Environment.GetEnvironmentVariable("DB_CONFIG_PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        services.AddSingleton(new DatabaseRuntimeOptions(enableMonitorConfig, resolvedPath.Path) { AllowUnconfigured = allowUnconfigured });
        services.AddSingleton<IJsonResultSerializer, JsonResultSerializer>();
        services.AddSingleton(DatabaseExecutionOptions.FromEnvironment());
        services.TryAddSingleton<ConfigurationChangeSignal>();
        services.AddSingleton<DatabaseExecutionCoordinator>();
        services.AddTransient<SqlDatabaseSessions>();
        services.AddTransient<SqlRequestExecutor>();
        services.AddSingleton<SqlSugarConnectionSettings>();
        services.AddTransient<DatabaseMcpServer.Operations.SqlQueryOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.SqlCommandOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.SchemaReadOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.SchemaCommandOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.ConnectionOperations>();
        services.AddTransient<DatabaseQueryService>();
        services.AddTransient<ObjectMetadataService>();
        services.AddTransient<SqlExplainService>();
        services.AddTransient<SqlExportService>();
        services.AddSingleton<SqlServerPlanMode>();
        services.AddTransient<AtomicCommandService>();
        services.AddSingleton<AtomicTransactionCompletion>();
        services.AddTransient<MongoCollectionService>();
        services.AddSingleton<DatabaseConnectionProbe>();
        services.AddSingleton<DatabaseMetadataCache>();
        services.AddSingleton<IDatabaseHelperService, DatabaseHelper>();
        services.AddSingleton<IDatabaseOptimizationStrategyFactory, DatabaseOptimizationStrategyFactory>();
        services.AddSingleton<ISqlSugarClientFactory, SqlSugarClientFactory>();
        services.AddSingleton<ICurrentDatabaseStateStore>(serviceProvider =>
            new CurrentDatabaseStateStore(
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CurrentDatabaseStateStore>>(),
                cliToolMode,
                currentDatabaseStateFilePath));
        services.AddSingleton<IDatabaseConfigService, DatabaseConfigService>();
        services.AddSingleton<ConfigurationFileStore>();
        services.AddSingleton<DatabaseConfigurationApplicationService>();
        services.AddSingleton<DatabaseConfigFileMonitorService>();
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseConfigFileMonitorService>());
        services.AddTransient(sp => new ConfigurationRuntimeApplier(sp.GetRequiredService<ConfigurationFileStore>(),
            () => sp.GetRequiredService<IDatabaseConfigService>(),
            () => sp.GetRequiredService<DatabaseConfigFileMonitorService>().EnsureMonitorStarted()));
        return services;
    }

    public static IServiceCollection AddDatabaseMcpToolServices(this IServiceCollection services)
    {
        services.TryAddSingleton<ToolCatalog>();
        services.TryAddSingleton<ConnectionStringBuilder>();
        services.TryAddSingleton<CliWebToolService>();
        services.TryAddTransient<DatabaseResources>();
        services.TryAddTransient<DatabasePrompts>();

        foreach (var toolType in DatabaseMcpToolCatalog.ToolTypes)
        {
            services.TryAddTransient(toolType);
        }

        return services;
    }

    public static IMcpServerBuilder AddDatabaseMcpServer(this IServiceCollection services)
    {
        services.AddDatabaseMcpToolServices();
        // Validate even when no request ever arrives.
        var profile = ToolExposurePolicy.ParseProfile(Environment.GetEnvironmentVariable("DB_MCP_TOOL_PROFILE"));
        services.TryAddSingleton(sp => new ToolExposurePolicy(profile, sp.GetRequiredService<ConfigurationChangeSignal>()));
        var validation = new DatabaseMcpServer.Filters.ToolRequestValidationFilter();

        var builder = services
            .AddMcpServer(options =>
            {
                options.Capabilities ??= new();
                options.Capabilities.Tools = new() { ListChanged = true };
                options.Capabilities.Resources = new();
                options.Capabilities.Prompts = new();
                options.Capabilities.Completions = new();
                options.ServerInstructions =
                    "Call list_databases or get_database_capabilities before assuming provider SQL features. " +
                    "Inspect schema with list_objects and get_table_schema before writing queries, bind SQL values via parameters, " +
                    "and check limitReached/truncated before assuming completeness. Use SqlSugar-backed mongo_* tools for MongoDb connections and verify writeOutcome after timeouts.";
                // MCP 2.0: advertise server identity via ServerInfo (Title/Description/WebsiteUrl).
                options.ServerInfo = new Implementation
                {
                    Name = "DatabaseMcpServer",
                    Version = DatabaseMcpServer.Hosting.ApplicationVersion.Value,
                    Title = "Database MCP Server",
                    Description = "Multi-database MCP server powered by SqlSugar, exposing connection, schema, query, command, and MongoDB tools over stdio.",
                    WebsiteUrl = "https://github.com/ttcc666/DatabaseMcpServer"
                };
            })
            .WithStdioServerTransport()
            .WithResources<DatabaseResources>()
            .WithPrompts<DatabasePrompts>()
            .WithCompleteHandler((request, _) =>
            {
                var config = request.Services?.GetService<IDatabaseConfigService>();
                var param = request.Params;
                if (config != null && param?.Argument.Name is "databaseName" or "database_name")
                {
                    var prefix = param.Argument.Value ?? string.Empty;
                    var matches = config.GetAllConnections()
                        .Select(c => c.Name)
                        .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList();
                    return ValueTask.FromResult(new CompleteResult
                    {
                        Completion = new() { Values = matches.Take(100).ToList(), Total = matches.Count, HasMore = matches.Count > 100 }
                    });
                }
                return ValueTask.FromResult(new CompleteResult { Completion = new() { Values = [], Total = 0, HasMore = false } });
            })
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(next => (context, cancellationToken) => validation.InvokeAsync(context, cancellationToken,
                    (request, token) => next(request, token)));
                filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    var provider = context.Services ?? context.Server.Services!;
                    var policy = provider.GetRequiredService<ToolExposurePolicy>();
                    var catalog = provider.GetRequiredService<ToolCatalog>();
                    var configuration = provider.GetRequiredService<IDatabaseConfigService>();
                    var options = provider.GetRequiredService<DatabaseExecutionOptions>();
                    policy.Observe(context.Server, catalog, configuration, options);
                    var selected = policy.Select(catalog, configuration, options);
                    result.Tools = result.Tools.Where(tool => selected.Contains(tool.Name) ||
                        !catalog.TryGetTool(tool.Name, out _)).ToList();
                    foreach (var tool in result.Tools)
                        if (catalog.TryGetTool(tool.Name, out var descriptor)) tool.InputSchema = descriptor.ProtocolTool.InputSchema;
                    return result;
                });
            });

        // Prefer explicit WithTools<T>() registration (AOT-friendly) over assembly scanning.
        foreach (var registration in DatabaseMcpToolCatalog.Registrations)
        {
            builder = registration.RegisterWithMcp(builder);
        }

        return builder;
    }

}
