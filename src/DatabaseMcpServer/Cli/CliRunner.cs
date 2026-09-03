using System.Globalization;
using System.Text;
using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Cli;

internal sealed class CliRunner
{
    private const int SuccessExitCode = 0;
    private const int ToolFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;

    private readonly CliToolCatalog _catalog;
    private readonly CliCommandParser _parser;
    private readonly CliConfigCommandHandler _configCommandHandler;
    private readonly ICliWebHost _webHost;
    private readonly string? _currentDatabaseStateFilePath;
    private readonly bool? _enableMonitorConfig;

    public CliRunner(bool? enableMonitorConfig = null)
        : this(new CliToolCatalog(), new CliConfigCommandHandler(), new CliWebHost(), null, enableMonitorConfig)
    {
    }

    internal CliRunner(
        CliToolCatalog catalog,
        CliConfigCommandHandler configCommandHandler,
        ICliWebHost webHost,
        string? currentDatabaseStateFilePath,
        bool? enableMonitorConfig = null)
    {
        _catalog = catalog;
        _parser = new CliCommandParser(catalog);
        _configCommandHandler = configCommandHandler;
        _webHost = webHost;
        _currentDatabaseStateFilePath = currentDatabaseStateFilePath;
        _enableMonitorConfig = enableMonitorConfig;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var parseResult = _parser.Parse(args);

        try
        {
            switch (parseResult.Kind)
            {
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
                    await WriteToolListAsync(stderr);
                    return SuccessExitCode;

                case CliCommandKind.ToolHelp:
                    await WriteToolHelpAsync(parseResult.Tool!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.ToolInvoke:
                    return await ExecuteToolAsync(parseResult, stdout, stderr);

                case CliCommandKind.InitHelp:
                    await WriteCommandHelpAsync(parseResult.Command!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.InitInvoke:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Initialize(
                            parseResult.ConfigPath,
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                        stdout);

                case CliCommandKind.ConfigRootHelp:
                    await stderr.WriteAsync(CliBuiltinCommandCatalog.WriteConfigRootHelp());
                    return SuccessExitCode;

                case CliCommandKind.ConfigHelp:
                    await WriteCommandHelpAsync(parseResult.Command!, stderr);
                    return SuccessExitCode;

                case CliCommandKind.ConfigList:
                    return await ExecuteConfigPayloadAsync(_configCommandHandler.List(parseResult.ConfigPath), stdout);

                case CliCommandKind.ConfigShow:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Show(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                        stdout);

                case CliCommandKind.ConfigPresets:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.ListPresets(),
                        stdout);

                case CliCommandKind.ConfigPreset:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.ShowPreset(GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "db-type")),
                        stdout);

                case CliCommandKind.ConfigCreate:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.CreateFromPreset(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "from-preset"),
                            GetOptionalStringOption(parseResult.OptionValues, "name"),
                            GetOptionalStringOption(parseResult.OptionValues, "connection-string"),
                            GetOptionalStringOption(parseResult.OptionValues, "description"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "set-default"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "enable-dangerous-operations"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "print-only")),
                        stdout);

                case CliCommandKind.ConfigAdd:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Add(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"),
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "db-type"),
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "connection-string"),
                            GetOptionalStringOption(parseResult.OptionValues, "description"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "set-default"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "enable-dangerous-operations")),
                        stdout);

                case CliCommandKind.ConfigRename:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Rename(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"),
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "new-name")),
                        stdout);

                case CliCommandKind.ConfigUpdate:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Update(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"),
                            GetOptionalStringOption(parseResult.OptionValues, "db-type"),
                            GetOptionalStringOption(parseResult.OptionValues, "connection-string"),
                            GetOptionalStringOption(parseResult.OptionValues, "description"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "clear-description"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "set-default"),
                            HasOption(parseResult.OptionValues, "db-type"),
                            HasOption(parseResult.OptionValues, "connection-string"),
                            HasOption(parseResult.OptionValues, "description"),
                            HasOption(parseResult.OptionValues, "clear-description"),
                            HasOption(parseResult.OptionValues, "set-default"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "enable-dangerous-operations"),
                            HasOption(parseResult.OptionValues, "enable-dangerous-operations")),
                        stdout);

                case CliCommandKind.ConfigClone:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Clone(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"),
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "new-name"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "set-default")),
                        stdout);

                case CliCommandKind.ConfigRemove:
                    if (parseResult.Command!.RequiresConfirmation && !parseResult.ConfirmationAccepted)
                    {
                        await stderr.WriteLineAsync($"命令 '{parseResult.Command.Name}' 需要显式确认。请追加 '--yes'。");
                        return UsageErrorExitCode;
                    }

                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Remove(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                        stdout);

                case CliCommandKind.ConfigSetDefault:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.SetDefault(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                        stdout);

                case CliCommandKind.ConfigUse:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Use(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                        stdout);

                case CliCommandKind.ConfigTest:
                    return await ExecuteConfigPayloadAsync(
                        await _configCommandHandler.TestAsync(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                        stdout);

                case CliCommandKind.ConfigValidate:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Validate(parseResult.ConfigPath),
                        stdout);

                case CliCommandKind.ConfigDoctor:
                    return await ExecuteConfigPayloadAsync(
                        await _configCommandHandler.DoctorAsync(
                            parseResult.ConfigPath,
                            GetOptionalStringOption(parseResult.OptionValues, "name"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "test-connections"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "fix-suggestions"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "summary-only")),
                        stdout);

                case CliCommandKind.ConfigExport:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Export(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "output"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                        stdout);

                case CliCommandKind.ConfigImport:
                    return await ExecuteConfigPayloadAsync(
                        _configCommandHandler.Import(
                            parseResult.ConfigPath,
                            GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "input"),
                            GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                        stdout);

                case CliCommandKind.Error:
                    await stderr.WriteLineAsync(parseResult.ErrorMessage);
                    await WriteRootHelpAsync(stderr);
                    return UsageErrorExitCode;

                default:
                    await stderr.WriteLineAsync("未知 CLI 状态。");
                    return UsageErrorExitCode;
            }
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

    public static Task WriteRootHelpAsync(TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  DatabaseMcpServer [--enable-monitor-config true|false]");
        builder.AppendLine("  DatabaseMcpServer [--enable-monitor-config true|false] -web [--config path] [--port 0-65535] [--no-browser] [--enable-monitor-config true|false]");
        builder.AppendLine("  DatabaseMcpServer init [--config path] [--force]");
        builder.AppendLine("  DatabaseMcpServer config <subcommand> [options]");
        builder.AppendLine("  DatabaseMcpServer tool list");
        builder.AppendLine("  DatabaseMcpServer tool help <tool_name>");
        builder.AppendLine("  DatabaseMcpServer tool <tool_name> [--option value...] [--config path] [--yes]");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("  No arguments starts the stdio MCP server.");
        builder.AppendLine("  --enable-monitor-config true|false enables or disables databases.json monitoring for this process; it overrides ENABLE_MONITOR_CONFIG and enableMonitorConfig.");
        builder.AppendLine("  -web starts a local Web configuration UI on localhost.");
        builder.AppendLine("  CLI help and metadata are written to stderr.");
        builder.AppendLine("  CLI command results are written to stdout as JSON.");
        builder.AppendLine("  In CLI tool mode, switch_database persists the current connection per resolved config path.");
        builder.AppendLine("  Use config use / config set-default when you want to change the default connection in databases.json.");
        builder.AppendLine();
        builder.AppendLine("Examples:");
        builder.AppendLine("  DatabaseMcpServer -web");
        builder.AppendLine("  DatabaseMcpServer -web --config '.\\local-databases.json' --no-browser");
        builder.AppendLine("  DatabaseMcpServer init");
        builder.AppendLine("  DatabaseMcpServer config list");
        builder.AppendLine("  DatabaseMcpServer tool list");
        builder.AppendLine("  DatabaseMcpServer tool get_database_config --config '.\\local-databases.json'");
        builder.AppendLine("  DatabaseMcpServer tool switch_database --database-name 'sqlite-local' --config '.\\local-databases.json'");
        return stderr.WriteAsync(builder.ToString());
    }

    private async Task<int> ExecuteToolAsync(CliParseResult parseResult, TextWriter stdout, TextWriter stderr)
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

        var resolution = CliConfigurationPathResolver.Resolve(parseResult.ConfigPath);
        if (!resolution.Success)
        {
            await stderr.WriteLineAsync(resolution.ErrorMessage);
            return UsageErrorExitCode;
        }

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var originalConsoleOut = Console.Out;
        var originalConsoleError = Console.Error;
        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", resolution.Path);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            var builder = DatabaseHostBuilderFactory.CreateBaseBuilder(
                [],
                silentLogs: true,
                cliToolMode: true,
                currentDatabaseStateFilePath: _currentDatabaseStateFilePath,
                enableMonitorConfig: _enableMonitorConfig);
            using var host = builder.Build();
            var toolInstance = host.Services.GetRequiredService(tool.ToolType);
            var payload = await CliToolInvoker.InvokeAsync(tool, toolInstance, arguments);

            await stdout.WriteAsync(payload);
            return CliToolInvoker.IsToolFailure(payload) ? ToolFailureExitCode : SuccessExitCode;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync(CliToolInvoker.Unwrap(ex).Message);
            return UsageErrorExitCode;
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
            Console.SetError(originalConsoleError);
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
        }
    }

    internal static object[] BindArguments(CliToolMetadata tool, IReadOnlyDictionary<string, string?> optionValues)
    {
        return CliToolInvoker.BindArguments(tool, optionValues);
    }

    private static async Task<int> ExecuteConfigPayloadAsync(string payload, TextWriter stdout)
    {
        await stdout.WriteAsync(payload);
        return CliToolInvoker.IsToolFailure(payload) ? ToolFailureExitCode : SuccessExitCode;
    }

    private Task WriteToolListAsync(TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Available tools:");
        foreach (var tool in _catalog.Tools)
        {
            builder.AppendLine($"  {tool.Name} - {tool.Description}");
        }

        return stderr.WriteAsync(builder.ToString());
    }

    private static Task WriteToolRootHelpAsync(TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  DatabaseMcpServer tool list");
        builder.AppendLine("  DatabaseMcpServer tool help <tool_name>");
        builder.AppendLine("  DatabaseMcpServer tool <tool_name> [--option value...] [--config path] [--yes]");
        builder.AppendLine();
        builder.AppendLine("Global options:");
        builder.AppendLine("  --config <path>   Override config path for this invocation.");
        builder.AppendLine("  --yes             Required for write or high-risk schema tools.");
        builder.AppendLine("  --help, -h        Show help.");
        builder.AppendLine();
        builder.AppendLine("Config resolution order:");
        builder.AppendLine("  1. --config");
        builder.AppendLine("  2. ./databases.json");
        builder.AppendLine("  3. ./local-databases.json");
        builder.AppendLine("  4. DB_CONFIG_PATH");
        builder.AppendLine("  5. %USERPROFILE%/.database-mcp/databases.json");
        builder.AppendLine();
        builder.AppendLine("Current database behavior:");
        builder.AppendLine("  switch_database persists the current connection per resolved config path for later CLI tool invocations.");
        builder.AppendLine("  config use / config set-default changes the default connection stored in databases.json.");
        return stderr.WriteAsync(builder.ToString());
    }

    private static Task WriteToolHelpAsync(CliToolMetadata tool, TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Tool: {tool.Name}");
        builder.AppendLine(tool.Description);
        builder.AppendLine();
        builder.AppendLine("Usage:");
        builder.Append($"  DatabaseMcpServer tool {tool.Name}");
        foreach (var parameter in tool.Parameters)
        {
            builder.Append(parameter.IsRequired
                ? $" --{parameter.OptionName} <{parameter.DisplayTypeName}>"
                : $" [--{parameter.OptionName} <{parameter.DisplayTypeName}>]");
        }

        builder.AppendLine(" [--config path] [--yes]");
        builder.AppendLine();
        builder.AppendLine($"Requires --yes: {(tool.RequiresConfirmation ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine("Options:");
        if (tool.Parameters.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var parameter in tool.Parameters)
            {
                var requiredText = parameter.IsRequired ? "required" : "optional";
                var defaultText = parameter.IsRequired
                    ? string.Empty
                    : $" default: {FormatDefaultValue(parameter.DefaultValue)}";
                builder.AppendLine(
                    $"  --{parameter.OptionName} <{parameter.DisplayTypeName}>  {requiredText}{defaultText}");
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    builder.AppendLine($"      {parameter.Description}");
                }
            }
        }

        builder.AppendLine("  --config <path>  optional");
        builder.AppendLine("      Override config path for this invocation.");
        builder.AppendLine("  --yes  optional");
        builder.AppendLine("      Confirm execution for write or high-risk schema tools.");
        builder.AppendLine("  --help, -h");
        builder.AppendLine("      Show this help.");

        return stderr.WriteAsync(builder.ToString());
    }

    private static Task WriteCommandHelpAsync(CliCommandMetadata command, TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {command.Name}");
        builder.AppendLine(command.Description);
        builder.AppendLine();
        builder.AppendLine("Usage:");
        builder.AppendLine($"  {command.Usage}");
        builder.AppendLine();
        builder.AppendLine($"Requires --yes: {(command.RequiresConfirmation ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine("Options:");
        if (command.Options.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var option in command.Options)
            {
                var requiredText = option.IsRequired ? "required" : "optional";
                var defaultText = option.IsRequired
                    ? string.Empty
                    : $" default: {FormatDefaultValue(option.DefaultValue)}";
                builder.AppendLine($"  --{option.OptionName} <{option.DisplayTypeName}>  {requiredText}{defaultText}");
                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    builder.AppendLine($"      {option.Description}");
                }
            }
        }

        builder.AppendLine("  --config <path>  optional");
        builder.AppendLine("      Override the target config path for this invocation.");
        if (command.RequiresConfirmation)
        {
            builder.AppendLine("  --yes  optional");
            builder.AppendLine("      Confirm execution for destructive config commands.");
        }

        builder.AppendLine("  --help, -h");
        builder.AppendLine("      Show this help.");
        return stderr.WriteAsync(builder.ToString());
    }

    private static string FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text when text.Length == 0 => "\"\"",
            string text => text,
            bool boolValue => boolValue ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool GetBoolOption(
        CliCommandMetadata command,
        IReadOnlyDictionary<string, string?>? optionValues,
        string optionName)
    {
        if (optionValues == null || !optionValues.TryGetValue(optionName, out var rawValue))
        {
            var option = command.Options.First(item => string.Equals(item.OptionName, optionName, StringComparison.Ordinal));
            return option.DefaultValue is bool defaultBool && defaultBool;
        }

        if (bool.TryParse(rawValue, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"选项 '--{optionName}' 需要 bool 值。");
    }

    private static int? GetOptionalIntOption(
        CliCommandMetadata command,
        IReadOnlyDictionary<string, string?>? optionValues,
        string optionName)
    {
        if (optionValues == null || !optionValues.TryGetValue(optionName, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            if (value is < 0 or > 65535)
            {
                throw new InvalidOperationException($"选项 '--{optionName}' 必须在 0-65535 之间。命令: {command.Name}");
            }

            return value;
        }

        throw new InvalidOperationException($"选项 '--{optionName}' 需要整数值。命令: {command.Name}");
    }

    private static string GetRequiredStringOption(
        CliCommandMetadata command,
        IReadOnlyDictionary<string, string?>? optionValues,
        string optionName)
    {
        if (optionValues != null && optionValues.TryGetValue(optionName, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        throw new InvalidOperationException($"缺少必填选项 '--{optionName}'。命令: {command.Name}");
    }

    private static string? GetOptionalStringOption(IReadOnlyDictionary<string, string?>? optionValues, string optionName)
    {
        return optionValues != null && optionValues.TryGetValue(optionName, out var rawValue)
            ? rawValue
            : null;
    }

    private static bool HasOption(IReadOnlyDictionary<string, string?>? optionValues, string optionName)
    {
        return optionValues != null && optionValues.ContainsKey(optionName);
    }
}
