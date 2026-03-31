using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose);

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

SqlSugarProviderWarmup.Warmup(serilogLogger);
builder.Services.AddDatabaseMcpApplicationServices();
builder.Services.AddDatabaseMcpServer();

await builder.Build().RunAsync();
