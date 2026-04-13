using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Hosting;
using Microsoft.Extensions.Hosting;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    if (args.Length == 0)
    {
        var builder = DatabaseHostBuilderFactory.CreateBaseBuilder(args);
        builder.Services.AddDatabaseMcpServer();
        await builder.Build().RunAsync();
        return 0;
    }

    var cliRunner = new CliRunner();
    return await cliRunner.RunAsync(args, Console.Out, Console.Error);
}
