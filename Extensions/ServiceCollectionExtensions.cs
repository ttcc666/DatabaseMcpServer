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

    public static IMcpServerBuilder AddDatabaseMcpServer(this IServiceCollection services)
    {
        return services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<ConnectionTools>()
            .WithTools<SchemaTools>()
            .WithTools<QueryTools>()
            .WithTools<CommandTools>()
            .WithTools<ExcelExportTools>()
            .WithTools<DocumentationTools>();
    }
}
