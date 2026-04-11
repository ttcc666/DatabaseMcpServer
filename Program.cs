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

    if (string.Equals(args[0], "tool", StringComparison.Ordinal))
    {
        var cliRunner = new CliRunner();
        return await cliRunner.RunAsync(args.Skip(1).ToArray(), Console.Out, Console.Error);
    }

    if (string.Equals(args[0], "--help", StringComparison.Ordinal) ||
        string.Equals(args[0], "-h", StringComparison.Ordinal))
    {
        await CliRunner.WriteRootHelpAsync(Console.Error);
        return 0;
    }

    await CliRunner.WriteRootHelpAsync(Console.Error);
    return 2;
}
