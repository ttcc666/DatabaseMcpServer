using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies;
using DatabaseMcpServer.Strategies.DBSetting;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Documentation;
using DatabaseMcpServer.Tools.Export;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseMcpApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IJsonResultSerializer, JsonResultSerializer>();
        services.AddSingleton<IDatabaseHelperService, DatabaseHelper>();
        services.AddSingleton<IDatabaseOptimizationStrategyFactory, DatabaseOptimizationStrategyFactory>();
        services.AddSingleton<ISqlSugarClientFactory, SqlSugarClientFactory>();
        services.AddSingleton<IDatabaseConfigService, DatabaseConfigService>();
        services.AddSingleton<DatabaseDocumentationStrategyFactory>();
        services.AddSingleton<IDatabaseDocumentationService, DatabaseDocumentationService>();
        return services;
    }

    public static IServiceCollection AddDatabaseMcpToolServices(this IServiceCollection services)
    {
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
            .AddMcpServer()
            .WithStdioServerTransport();

        foreach (var registration in DatabaseMcpToolCatalog.Registrations)
        {
            builder = registration.RegisterWithMcp(builder);
        }

        return builder;
    }
}
