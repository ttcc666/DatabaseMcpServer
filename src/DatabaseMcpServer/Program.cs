using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Hosting;
using Microsoft.Extensions.Hosting;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    var startup = DatabaseStartupArguments.Parse(args);
    if (startup.ErrorMessage != null)
    {
        await Console.Error.WriteLineAsync(startup.ErrorMessage);
        await CliRunner.WriteRootHelpAsync(Console.Error);
        return 2;
    }

    if (startup.RunMcpServer)
    {
        var builder = DatabaseHostBuilderFactory.CreateBaseBuilder(
            [],
            enableMonitorConfig: startup.EnableMonitorConfig);
        builder.Services.AddDatabaseMcpServer();
        await builder.Build().RunAsync();
        return 0;
    }

    var cliRunner = new CliRunner(enableMonitorConfig: startup.EnableMonitorConfig);
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += cancel;
    try { return await cliRunner.RunAsync(startup.RemainingArgs, Console.Out, Console.Error, cancellation.Token); }
    finally { Console.CancelKeyPress -= cancel; }
}
