using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DatabaseMcpServer.Hosting;

internal static class DatabaseHostBuilderFactory
{
    public static HostApplicationBuilder CreateBaseBuilder(
        string[] args,
        bool silentLogs = false,
        bool cliToolMode = false,
        string? currentDatabaseStateFilePath = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();

        var serilogLogger = CreateLogger(silentLogs);

        builder.Services.AddSerilog(serilogLogger);

        SqlSugarProviderWarmup.Warmup(serilogLogger);
        builder.Services.AddDatabaseMcpApplicationServices(cliToolMode, currentDatabaseStateFilePath);
        builder.Services.AddDatabaseMcpToolServices();

        return builder;
    }

    internal static Serilog.ILogger CreateLogger(bool silentLogs)
    {
        var loggerConfig = new LoggerConfiguration();

        if (silentLogs)
        {
            loggerConfig.MinimumLevel.Fatal();
            return loggerConfig.CreateLogger();
        }

        loggerConfig
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

        return loggerConfig.CreateLogger();
    }
}
