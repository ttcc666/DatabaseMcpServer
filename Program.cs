using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

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

builder.Services.AddSerilog(loggerConfig.CreateLogger());

// Register services for dependency injection
builder.Services.AddSingleton<IDatabaseHelperService, DatabaseHelper>();
builder.Services.AddSingleton<IDatabaseConfigService, DatabaseConfigService>();

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ConnectionTools>()
    .WithTools<SchemaTools>()
    .WithTools<QueryTools>()
    .WithTools<CommandTools>();

await builder.Build().RunAsync();