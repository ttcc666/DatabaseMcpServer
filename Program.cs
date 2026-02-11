using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Documentation;
using DatabaseMcpServer.Tools.Export;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Reflection;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                     standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose);

// Add Seq sink if configured
var seqServerUrl = Environment.GetEnvironmentVariable("SEQ_SERVER_URL");
if (!string.IsNullOrWhiteSpace(seqServerUrl))
{
    var seqApiKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");
    if (!string.IsNullOrWhiteSpace(seqApiKey))
    {
        loggerConfig.WriteTo.Seq(seqServerUrl, apiKey: seqApiKey);
    }
    else
    {
        loggerConfig.WriteTo.Seq(seqServerUrl);
    }
}

var serilogLogger = loggerConfig.CreateLogger();
builder.Services.AddSerilog(serilogLogger);

WarmupSqlSugarProviders(serilogLogger);

// Register services for dependency injection
builder.Services.AddSingleton<IDatabaseHelperService, DatabaseHelper>();
builder.Services.AddSingleton<IDatabaseConfigService, DatabaseConfigService>();
builder.Services.AddSingleton<DatabaseDocumentationStrategyFactory>();
builder.Services.AddSingleton<IDatabaseDocumentationService, DatabaseDocumentationService>();

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ConnectionTools>()
    .WithTools<SchemaTools>()
    .WithTools<QueryTools>()
    .WithTools<CommandTools>()
    .WithTools<ExcelExportTools>()
    .WithTools<DocumentationTools>();

await builder.Build().RunAsync();

static void WarmupSqlSugarProviders(Serilog.ILogger? logger)
{
    // 预加载部分 SqlSugar Provider，避免裁剪/单文件发布遗漏
    var providers = new (string AssemblyName, string? ProviderTypeName)[]
    {
        ("SqlSugar.ClickHouseCore", "ClickHouseProvider"),
        ("SqlSugar.MongoDbCore", "MongoDbProvider"),
        ("SqlSugar.GaussDBNativeCore", "GaussDBProvider"),
        ("SqlSugar.OceanBaseForOracleCore", "OceanBaseForOracleProvider")
    };

    foreach (var (assemblyName, providerTypeName) in providers)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);

            if (providerTypeName != null)
            {
                Type? providerType = null;
                foreach (var type in assembly.GetTypes())
                {
                    if (string.Equals(type.Name, providerTypeName, StringComparison.OrdinalIgnoreCase))
                    {
                        providerType = type;
                        break;
                    }
                }

                if (providerType != null)
                {
                    _ = providerType.Assembly;
                    logger?.Debug("预加载 SqlSugar Provider: {Provider} ({Assembly})", providerTypeName, assemblyName);
                }
                else
                {
                    logger?.Warning("未找到 Provider 类型 {Provider} 于 {Assembly}", providerTypeName, assemblyName);
                }
            }
            else
            {
                logger?.Debug("预加载 SqlSugar 程序集: {Assembly}", assemblyName);
            }
        }
        catch (Exception ex)
        {
            logger?.Warning(ex, "预加载 SqlSugar Provider 失败: {Assembly}", assemblyName);
        }
    }
}
