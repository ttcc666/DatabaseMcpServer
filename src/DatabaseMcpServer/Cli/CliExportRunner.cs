using System.Text.Json;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Cli;

internal static class CliExportRunner
{
    public static async Task<int> RunAsync(CliParseResult parsed, TextWriter stdout, CancellationToken token)
    {
        var resolution = ConfigurationPathResolver.Resolve(parsed.ConfigPath);
        if (!resolution.Success) throw new InvalidOperationException(resolution.ErrorMessage);
        var args = parsed.OptionValues!;
        var path = Path.GetFullPath(args["output"]!);
        var overwrite = args.GetValueOrDefault("overwrite") == "true";
        if (!overwrite && File.Exists(path)) throw new IOException("目标文件已存在；使用 --overwrite 显式覆盖。");
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".dbmcp-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var host = DatabaseHostBuilderFactory.CreateBaseBuilder([], silentLogs: true, cliToolMode: true,
                enableMonitorConfig: false, configPath: resolution.Path).Build();
            var parameters = args.TryGetValue("parameters", out var json) ? JsonSerializer.Deserialize<DatabaseParameter[]>(json!, new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true }) : null;
            object result;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                result = await host.Services.GetRequiredService<SqlExportService>().Export(file, args["sql"]!, parameters,
                    args.GetValueOrDefault("format") ?? "jsonl", args.GetValueOrDefault("database-name"),
                    ReadInt(args, "max-rows", 100000), ReadInt(args, "max-file-mib", 256), ReadInt(args, "command-timeout-seconds", 30), token);
            }
            var payload = McpResultAdapter.Text(result);
            if (ToolInvoker.IsToolFailure(payload))
            {
                await stdout.WriteLineAsync(payload);
                return 1;
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite);
            using var summary = JsonDocument.Parse(payload);
            await stdout.WriteLineAsync(McpResultAdapter.Text(new { success = true, path, summary = summary.RootElement }));
            return 0;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static int ReadInt(IReadOnlyDictionary<string, string?> args, string name, int fallback)
        => args.TryGetValue(name, out var value) ? int.Parse(value!, System.Globalization.CultureInfo.InvariantCulture) : fallback;
}
