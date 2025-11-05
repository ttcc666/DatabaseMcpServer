using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

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