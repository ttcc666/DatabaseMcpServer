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
        var resolvedPath = CliConfigurationPathResolver.ResolveWritablePath(
            configPath ?? Environment.GetEnvironmentVariable("DB_CONFIG_PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        services.AddSingleton(new DatabaseRuntimeOptions(enableMonitorConfig, resolvedPath.Path));
        services.AddSingleton<IJsonResultSerializer, JsonResultSerializer>();
        services.AddSingleton(DatabaseExecutionOptions.FromEnvironment());
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
        services.AddHostedService<DatabaseConfigFileMonitorService>();
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
        var toolMetadata = new CliToolCatalog().Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var argumentNames = toolMetadata.Values.ToDictionary(tool => tool.Name,
            tool => tool.Parameters.Select(parameter => parameter.ParameterName).ToHashSet(StringComparer.Ordinal));

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
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                if (context.Params is { } request && argumentNames.TryGetValue(request.Name, out var allowed))
                {
                    var arguments = request.Arguments ?? new Dictionary<string, System.Text.Json.JsonElement>();
                    var unknown = arguments.Keys.FirstOrDefault(name => !allowed.Contains(name));
                    if (unknown != null)
                    {
                        if (request.Name.StartsWith("mongo_", StringComparison.Ordinal))
                            return MongoCollectionService.ToToolResult(new() { Success = false, ErrorCode = 1004,
                                ErrorMessage = "包含未知参数，请按当前 tools/list schema 调用。",
                                WriteOutcome = CliWriteProtection.RequiresConfirmation(request.Name) ? "not-executed" : null });
                        return new CallToolResult { IsError = true, Content = [new TextContentBlock
                        {
                            Text = System.Text.Json.JsonSerializer.Serialize(new { success = false, errorCode = 1004,
                                errorMessage = $"未知参数 '{unknown}'，请按当前 tools/list schema 调用。" })
                        }] };
                    }
                    if (request.Name.StartsWith("mongo_", StringComparison.Ordinal))
                    {
                        try
                        {
                            foreach (var parameter in toolMetadata[request.Name].Parameters)
                            {
                                if (!arguments.TryGetValue(parameter.ParameterName, out var value))
                                {
                                    if (parameter.IsRequired) throw MongoDocumentContract.Invalid("缺少必填 Mongo 工具参数。");
                                    continue;
                                }
                                if (parameter.IsJson) MongoDocumentContract.ValidateObject(value);
                                else if (value.ValueKind == System.Text.Json.JsonValueKind.Null && !parameter.IsRequired &&
                                    (!parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null)) continue;
                                else if (parameter.EffectiveType == typeof(string) && value.ValueKind == System.Text.Json.JsonValueKind.String) continue;
                                else if (parameter.EffectiveType == typeof(bool) && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) continue;
                                else if (parameter.EffectiveType == typeof(int) && value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out _)) continue;
                                else throw MongoDocumentContract.Invalid("Mongo 工具参数类型不符合当前 tools/list schema。");
                            }
                        }
                        catch (DatabaseMcpException ex)
                        {
                            return MongoCollectionService.ToToolResult(new() { Success = false, ErrorCode = (int)ex.ErrorCode,
                                ErrorMessage = ex.Message, WriteOutcome = CliWriteProtection.RequiresConfirmation(request.Name) ? "not-executed" : null });
                        }
                    }
                }
                var result = await next(context, cancellationToken);
                if (result.Content is [TextContentBlock text] && ToolResultStatus.IsFailure(text.Text))
                {
                    result.IsError = true;
                }

                return result;
            }));

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
