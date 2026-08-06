using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseMcpApplicationServices(
        this IServiceCollection services,
        bool cliToolMode = false,
        string? currentDatabaseStateFilePath = null)
    {
        services.AddSingleton<IJsonResultSerializer, JsonResultSerializer>();
        services.AddSingleton<IDatabaseHelperService, DatabaseHelper>();
        services.AddSingleton<IDatabaseOptimizationStrategyFactory, DatabaseOptimizationStrategyFactory>();
        services.AddSingleton<ISqlSugarClientFactory, SqlSugarClientFactory>();
        services.AddSingleton<ICurrentDatabaseStateStore>(serviceProvider =>
            new CurrentDatabaseStateStore(
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CurrentDatabaseStateStore>>(),
                cliToolMode,
                currentDatabaseStateFilePath));
        services.AddSingleton<IDatabaseConfigService, DatabaseConfigService>();
        return services;
    }

    public static IServiceCollection AddDatabaseMcpToolServices(this IServiceCollection services)
    {
        services.TryAddSingleton<CliToolCatalog>();
        services.TryAddSingleton<CliConnectionStringBuilder>();
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
            .WithStdioServerTransport();

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
        return version is null ? "3.6.6" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
