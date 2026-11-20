using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
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
        string? configPath = null)
    {
        var resolvedPath = ConfigurationPathResolver.ResolveWritablePath(
            configPath ?? Environment.GetEnvironmentVariable("DB_CONFIG_PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        services.AddSingleton(new DatabaseRuntimeOptions(enableMonitorConfig, resolvedPath.Path));
        services.AddSingleton<IJsonResultSerializer, JsonResultSerializer>();
        services.AddSingleton(DatabaseExecutionOptions.FromEnvironment());
        services.AddSingleton<DatabaseExecutionCoordinator>();
        services.AddTransient<SqlDatabaseSessions>();
        services.AddSingleton<SqlSugarConnectionSettings>();
        services.AddTransient<DatabaseMcpServer.Operations.SqlQueryOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.SqlCommandOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.SchemaOperations>();
        services.AddTransient<DatabaseMcpServer.Operations.ConnectionOperations>();
        services.AddTransient<DatabaseQueryService>();
        services.AddSingleton<MongoClientRegistry>();
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

        foreach (var toolType in DatabaseMcpToolCatalog.ToolTypes)
        {
            services.TryAddTransient(toolType);
        }

        return services;
    }

    public static IMcpServerBuilder AddDatabaseMcpServer(this IServiceCollection services)
    {
        services.AddDatabaseMcpToolServices();
        var validation = new DatabaseMcpServer.Filters.ToolRequestValidationFilter();

        var builder = services
            .AddMcpServer(options =>
            {
                // MCP 2.0: advertise server identity via ServerInfo (Title/Description/WebsiteUrl).
                options.ServerInfo = new Implementation
                {
                    Name = "DatabaseMcpServer",
                    Version = GetServerVersion(),
                    Title = "Database MCP Server",
                    Description = "Multi-database MCP server powered by SqlSugar, exposing connection, schema, query, and command tools over stdio.",
                    WebsiteUrl = "https://github.com/ttcc666/DatabaseMcpServer"
                };
            })
            .WithStdioServerTransport()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next =>
                (context, cancellationToken) => validation.InvokeAsync(context, cancellationToken,
                    (request, token) => next(request, token))));

        // Prefer explicit WithTools<T>() registration (AOT-friendly) over assembly scanning.
        foreach (var registration in DatabaseMcpToolCatalog.Registrations)
        {
            builder = registration.RegisterWithMcp(builder);
        }

        return builder;
    }

    private static string GetServerVersion()
    {
        var version = typeof(ServiceCollectionExtensions).Assembly.GetName().Version;
        return version is null ? "3.7.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
