using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using static DatabaseMcpServer.Cli.CliCommandOptions;
using static DatabaseMcpServer.Cli.CliHelpWriter;

namespace DatabaseMcpServer.Cli;

internal sealed class CliRunner
{
    private const int SuccessExitCode = 0;
    private const int ToolFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;

    private readonly ToolCatalog _catalog;
    private readonly CliCommandParser _parser;
    private readonly CliConfigurationCommandRunner _configurationRunner;
    private readonly ICliWebHost _webHost;
    private readonly string? _currentDatabaseStateFilePath;
    private readonly bool? _enableMonitorConfig;

    public CliRunner(bool? enableMonitorConfig = null)
        : this(new ToolCatalog(), new ConfigurationCommands(), new CliWebHost(), null, enableMonitorConfig)
    {
    }

    internal CliRunner(
        ToolCatalog catalog,
        ConfigurationCommands configCommandHandler,
        ICliWebHost webHost,
        string? currentDatabaseStateFilePath,
        bool? enableMonitorConfig = null)
    {
        _catalog = catalog;
        _parser = new CliCommandParser(catalog);
        _configurationRunner = new(configCommandHandler, currentDatabaseStateFilePath);
        _webHost = webHost;
        _currentDatabaseStateFilePath = currentDatabaseStateFilePath;
        _enableMonitorConfig = enableMonitorConfig;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        var parseResult = _parser.Parse(args);

        try
        {
            switch (parseResult.Kind)
            {
                case CliCommandKind.Version:
                    await stdout.WriteLineAsync(ApplicationVersion.Value);
                    return SuccessExitCode;
                case CliCommandKind.ExportHelp:
                    await WriteCommandHelpAsync(CliBuiltinCommandCatalog.Export, stderr);
                    return SuccessExitCode;
                case CliCommandKind.ExportInvoke:
                    try { return await CliExportRunner.RunAsync(parseResult, stdout, cancellationToken); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or OverflowException)
                    {
                        await stdout.WriteLineAsync(McpResultAdapter.Text(DatabaseMcpServer.Models.ApiResult<object>.CreateError(
                            "导出失败；检查目标文件、目录权限和参数。已有目标文件未被部分结果替换。", DatabaseMcpServer.Models.DatabaseErrorCode.FileGenerationFailed)));
                        return ToolFailureExitCode;
                    }
                case CliCommandKind.RootHelp:
                    if (!string.IsNullOrWhiteSpace(parseResult.ErrorMessage))
                    {
                        await stderr.WriteLineAsync(parseResult.ErrorMessage);
                        await WriteRootHelpAsync(stderr);
                        return UsageErrorExitCode;
                    }

                    await WriteRootHelpAsync(stderr);
                    return SuccessExitCode;

                case CliCommandKind.WebHelp:
                    await WriteCommandHelpAsync(parseResult.Command!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.WebInvoke:
                    await _webHost.RunAsync(
                        new CliWebCommandOptions(
                            parseResult.ConfigPath,
                            GetOptionalIntOption(parseResult.Command!, parseResult.OptionValues, "port"),
                            !GetBoolOption(parseResult.Command!, parseResult.OptionValues, "no-browser"),
                            _enableMonitorConfig
                                ?? (HasOption(parseResult.OptionValues, "enable-monitor-config")
                                    ? GetBoolOption(parseResult.Command!, parseResult.OptionValues, "enable-monitor-config")
                                    : null)),
                        stdout,
                        stderr);
                    return SuccessExitCode;

                case CliCommandKind.ToolRootHelp:
                    if (!string.IsNullOrWhiteSpace(parseResult.ErrorMessage))
                    {
                        await stderr.WriteLineAsync(parseResult.ErrorMessage);
                    }

                    await WriteToolRootHelpAsync(stderr);
                    return parseResult.ErrorMessage == null ? SuccessExitCode : UsageErrorExitCode;

                case CliCommandKind.ToolList:
                {
                    var all = parseResult.OptionValues?.GetValueOrDefault("all") == "true";
                    var tools = _catalog.Tools;
                    if (!all)
                    {
                        var resolution = ConfigurationPathResolver.Resolve(parseResult.ConfigPath);
                        if (!resolution.Success && resolution.Source != null)
                            throw new InvalidOperationException(resolution.ErrorMessage);
                        using var catalogHost = DatabaseHostBuilderFactory.CreateBaseBuilder([], silentLogs: true, cliToolMode: true,
                            configPath: resolution.Path, enableMonitorConfig: false, allowUnconfigured: true).Build();
                        var selected = ToolExposurePolicy.SelectFor(catalogHost.Services, _catalog);
                        tools = tools.Where(t => selected.Contains(t.Name)).ToArray();
                    }
                    if (parseResult.JsonOutput)
                        await stdout.WriteLineAsync(McpResultAdapter.Text(new { success = true, version = ApplicationVersion.Value,
                            tools = tools.Select(t => t.Describe()), capabilityCatalog = all
                                ? DatabaseMcpServer.Helpers.ProviderCapabilities.DescribeCatalog() : null }));
                    else await WriteToolListAsync(stderr, tools);
                    return SuccessExitCode;
                }

                case CliCommandKind.ToolHelp:
                    if (parseResult.JsonOutput)
                        await stdout.WriteLineAsync(McpResultAdapter.Text(new { success = true, tool = parseResult.Tool!.Describe() }));
                    else await WriteToolHelpAsync(parseResult.Tool!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.ToolInvoke:
                    return await ExecuteToolAsync(parseResult, stdout, stderr, cancellationToken);

                case CliCommandKind.InitHelp:
                    await WriteCommandHelpAsync(parseResult.Command!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.ConfigRootHelp:
                    await stderr.WriteAsync(CliBuiltinCommandCatalog.WriteConfigRootHelp());
                    return SuccessExitCode;

                case CliCommandKind.ConfigHelp:
                    await WriteCommandHelpAsync(parseResult.Command!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.InitInvoke:
                case CliCommandKind.ConfigList:
                case CliCommandKind.ConfigShow:
                case CliCommandKind.ConfigPresets:
                case CliCommandKind.ConfigPreset:
                case CliCommandKind.ConfigCreate:
                case CliCommandKind.ConfigAdd:
                case CliCommandKind.ConfigRename:
                case CliCommandKind.ConfigUpdate:
                case CliCommandKind.ConfigClone:
                case CliCommandKind.ConfigRemove:
                case CliCommandKind.ConfigSetDefault:
                case CliCommandKind.ConfigUse:
                case CliCommandKind.ConfigTest:
                case CliCommandKind.ConfigValidate:
                case CliCommandKind.ConfigDoctor:
                case CliCommandKind.ConfigExport:
                case CliCommandKind.ConfigImport:
                    return await _configurationRunner.RunAsync(parseResult, stdout, stderr, cancellationToken);

                case CliCommandKind.Error:
                    await stderr.WriteLineAsync(parseResult.ErrorMessage);
                    await WriteRootHelpAsync(stderr);
                    return UsageErrorExitCode;

                default:
                    await stderr.WriteLineAsync("未知 CLI 状态。");
                    return UsageErrorExitCode;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("操作已取消。");
            return 130;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            if (parseResult.Command != null)
            {
                await WriteCommandHelpAsync(parseResult.Command, stderr);
            }

            return UsageErrorExitCode;
        }
    }

    private async Task<int> ExecuteToolAsync(CliParseResult parseResult, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        var tool = parseResult.Tool!;
        if (tool.RequiresConfirmation && !parseResult.ConfirmationAccepted)
        {
            await stderr.WriteLineAsync($"tool '{tool.Name}' 需要显式确认。请追加 '--yes'。");
            return UsageErrorExitCode;
        }

        object[] arguments;
        try
        {
            arguments = BindArguments(tool, parseResult.OptionValues ?? new Dictionary<string, string?>());
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            await WriteToolHelpAsync(tool, stderr);
            return UsageErrorExitCode;
        }

        var resolution = ConfigurationPathResolver.Resolve(parseResult.ConfigPath);
        if (!resolution.Success)
        {
            await stderr.WriteLineAsync(resolution.ErrorMessage);
            return UsageErrorExitCode;
        }

        try
        {
            var builder = DatabaseHostBuilderFactory.CreateBaseBuilder(
                [],
                silentLogs: true,
                cliToolMode: true,
                currentDatabaseStateFilePath: _currentDatabaseStateFilePath,
                enableMonitorConfig: _enableMonitorConfig,
                configPath: resolution.Path);
            using var host = builder.Build();
            if (!ToolExposurePolicy.SelectFor(host.Services, _catalog).Contains(tool.Name))
                throw new InvalidOperationException($"tool '{tool.Name}' 未在当前配置和权限下启用。");
            var toolInstance = host.Services.GetRequiredService(tool.ToolType);
            var payload = await ToolInvoker.InvokeAsync(tool, toolInstance, arguments, cancellationToken);

            await stdout.WriteAsync(payload);
            if (cancellationToken.IsCancellationRequested) return 130;
            return ToolInvoker.IsToolFailure(payload) ? ToolFailureExitCode : SuccessExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("操作已取消。");
            return 130;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync(ToolInvoker.Unwrap(ex).Message);
            return UsageErrorExitCode;
        }
    }

    internal static object[] BindArguments(ToolDescriptor tool, IReadOnlyDictionary<string, string?> optionValues)
    {
        return ToolInvoker.BindArguments(tool, optionValues);
    }
}
