namespace DatabaseMcpServer.Cli;

internal enum CliCommandKind
{
    RootHelp,
    WebHelp,
    WebInvoke,
    ToolRootHelp,
    ToolList,
    ToolHelp,
    ToolInvoke,
    InitHelp,
    InitInvoke,
    ConfigRootHelp,
    ConfigHelp,
    ConfigList,
    ConfigShow,
    ConfigPresets,
    ConfigPreset,
    ConfigCreate,
    ConfigAdd,
    ConfigRename,
    ConfigUpdate,
    ConfigClone,
    ConfigRemove,
    ConfigSetDefault,
    ConfigUse,
    ConfigTest,
    ConfigValidate,
    ConfigDoctor,
    ConfigExport,
    ConfigImport,
    Error
}

internal sealed record CliParseResult(
    CliCommandKind Kind,
    CliToolMetadata? Tool = null,
    CliCommandMetadata? Command = null,
    IReadOnlyDictionary<string, string?>? OptionValues = null,
    string? ConfigPath = null,
    bool ConfirmationAccepted = false,
    string? ErrorMessage = null);

internal sealed class CliCommandParser
{
    private readonly CliToolCatalog _catalog;

    public CliCommandParser(CliToolCatalog catalog)
    {
        _catalog = catalog;
    }

    public CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliParseResult(CliCommandKind.RootHelp);
        }

        if (IsHelpToken(args[0]))
        {
            return new CliParseResult(CliCommandKind.RootHelp);
        }

        return args[0] switch
        {
            "-web" or "--web" => ParseStandaloneCommand(
                args.Skip(1).ToArray(),
                CliBuiltinCommandCatalog.Web,
                CliCommandKind.WebHelp,
                CliCommandKind.WebInvoke),
            "tool" => ParseToolCommand(args.Skip(1).ToArray()),
            "init" => ParseStandaloneCommand(
                args.Skip(1).ToArray(),
                CliBuiltinCommandCatalog.Init,
                CliCommandKind.InitHelp,
                CliCommandKind.InitInvoke),
            "config" => ParseConfigCommand(args.Skip(1).ToArray()),
            _ => new CliParseResult(
                CliCommandKind.Error,
                ErrorMessage: $"未知命令: '{args[0]}'。可用顶层命令: -web, tool, init, config")
        };
    }

    private CliParseResult ParseToolCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliParseResult(CliCommandKind.ToolRootHelp, ErrorMessage: "缺少 tool 名称。");
        }

        if (IsHelpToken(args[0]))
        {
            return new CliParseResult(CliCommandKind.ToolRootHelp);
        }

        if (string.Equals(args[0], "list", StringComparison.Ordinal))
        {
            return ParseNoCommandOptions(args.Skip(1).ToArray(), CliCommandKind.ToolList);
        }

        if (string.Equals(args[0], "help", StringComparison.Ordinal))
        {
            return ParseToolHelpCommand(args.Skip(1).ToArray());
        }

        if (!_catalog.TryGetTool(args[0], out var tool))
        {
            var suggestions = _catalog.GetClosestToolNames(args[0]);
            var suggestionText = suggestions.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}最接近的命令: {string.Join(", ", suggestions)}";
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: $"未知 tool: '{args[0]}'。{suggestionText}");
        }

        var parsedOptions = ParseOptions(
            args.Skip(1).ToArray(),
            tool.Parameters.Select(parameter => new CliCommandOptionMetadata(
                parameter.OptionName,
                parameter.Description,
                parameter.EffectiveType,
                parameter.IsRequired,
                parameter.DefaultValue)).ToArray());
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, Tool: tool, ErrorMessage: parsedOptions.ErrorMessage);
        }

        if (parsedOptions.HelpRequested)
        {
            return new CliParseResult(
                CliCommandKind.ToolHelp,
                Tool: tool,
                ConfigPath: parsedOptions.ConfigPath,
                ConfirmationAccepted: parsedOptions.YesAccepted);
        }

        return new CliParseResult(
            CliCommandKind.ToolInvoke,
            Tool: tool,
            OptionValues: parsedOptions.OptionValues,
            ConfigPath: parsedOptions.ConfigPath,
            ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private CliParseResult ParseNoCommandOptions(IReadOnlyList<string> args, CliCommandKind kind)
    {
        var parsedOptions = ParseOptions(args, []);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: parsedOptions.ErrorMessage);
        }

        return new CliParseResult(kind, ConfigPath: parsedOptions.ConfigPath, ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private CliParseResult ParseToolHelpCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliParseResult(CliCommandKind.ToolRootHelp);
        }

        if (IsHelpToken(args[0]))
        {
            return new CliParseResult(CliCommandKind.ToolRootHelp);
        }

        if (!_catalog.TryGetTool(args[0], out var tool))
        {
            var suggestions = _catalog.GetClosestToolNames(args[0]);
            var suggestionText = suggestions.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}最接近的命令: {string.Join(", ", suggestions)}";
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: $"未知 tool: '{args[0]}'。{suggestionText}");
        }

        var parsedOptions = ParseOptions(args.Skip(1).ToArray(), []);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, Tool: tool, ErrorMessage: parsedOptions.ErrorMessage);
        }

        return new CliParseResult(
            CliCommandKind.ToolHelp,
            Tool: tool,
            ConfigPath: parsedOptions.ConfigPath,
            ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private CliParseResult ParseStandaloneCommand(
        IReadOnlyList<string> args,
        CliCommandMetadata command,
        CliCommandKind helpKind,
        CliCommandKind invokeKind)
    {
        var parsedOptions = ParseOptions(args, command.Options);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, Command: command, ErrorMessage: parsedOptions.ErrorMessage);
        }

        return parsedOptions.HelpRequested
            ? new CliParseResult(helpKind, Command: command)
            : new CliParseResult(
                invokeKind,
                Command: command,
                OptionValues: parsedOptions.OptionValues,
                ConfigPath: parsedOptions.ConfigPath,
                ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private CliParseResult ParseConfigCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelpToken(args[0]))
        {
            return new CliParseResult(CliCommandKind.ConfigRootHelp);
        }

        if (string.Equals(args[0], "help", StringComparison.Ordinal))
        {
            if (args.Count == 1 || IsHelpToken(args[1]))
            {
                return new CliParseResult(CliCommandKind.ConfigRootHelp);
            }

            if (!CliBuiltinCommandCatalog.TryGetConfigCommand(args[1], out var helpCommand))
            {
                return new CliParseResult(CliCommandKind.Error, ErrorMessage: $"未知 config 命令: '{args[1]}'。");
            }

            var parsedHelpOptions = ParseOptions(args.Skip(2).ToArray(), []);
            if (parsedHelpOptions.ErrorMessage != null)
            {
                return new CliParseResult(CliCommandKind.Error, Command: helpCommand, ErrorMessage: parsedHelpOptions.ErrorMessage);
            }

            return new CliParseResult(CliCommandKind.ConfigHelp, Command: helpCommand);
        }

        if (!CliBuiltinCommandCatalog.TryGetConfigCommand(args[0], out var command))
        {
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: $"未知 config 命令: '{args[0]}'。");
        }

        var parsedOptions = ParseOptions(args.Skip(1).ToArray(), command.Options);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, Command: command, ErrorMessage: parsedOptions.ErrorMessage);
        }

        if (parsedOptions.HelpRequested)
        {
            return new CliParseResult(CliCommandKind.ConfigHelp, Command: command);
        }

        var kind = args[0] switch
        {
            "list" => CliCommandKind.ConfigList,
            "show" => CliCommandKind.ConfigShow,
            "presets" => CliCommandKind.ConfigPresets,
            "preset" => CliCommandKind.ConfigPreset,
            "create" => CliCommandKind.ConfigCreate,
            "add" => CliCommandKind.ConfigAdd,
            "rename" => CliCommandKind.ConfigRename,
            "update" => CliCommandKind.ConfigUpdate,
            "clone" => CliCommandKind.ConfigClone,
            "remove" => CliCommandKind.ConfigRemove,
            "set-default" => CliCommandKind.ConfigSetDefault,
            "use" => CliCommandKind.ConfigUse,
            "test" => CliCommandKind.ConfigTest,
            "validate" => CliCommandKind.ConfigValidate,
            "doctor" => CliCommandKind.ConfigDoctor,
            "export" => CliCommandKind.ConfigExport,
            "import" => CliCommandKind.ConfigImport,
            _ => CliCommandKind.Error
        };

        return new CliParseResult(
            kind,
            Command: command,
            OptionValues: parsedOptions.OptionValues,
            ConfigPath: parsedOptions.ConfigPath,
            ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private static ParsedOptions ParseOptions(
        IReadOnlyList<string> args,
        IReadOnlyList<CliCommandOptionMetadata> parameters)
    {
        var optionMap = parameters.ToDictionary(item => item.OptionName, StringComparer.Ordinal);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? configPath = null;
        var yesAccepted = false;
        var helpRequested = false;

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (IsHelpToken(token))
            {
                helpRequested = true;
                continue;
            }

            if (string.Equals(token, "--yes", StringComparison.Ordinal))
            {
                yesAccepted = true;
                continue;
            }

            if (string.Equals(token, "--config", StringComparison.Ordinal))
            {
                if (!TryReadOptionValue(args, ref i, out var value))
                {
                    return new ParsedOptions("选项 '--config' 缺少值。");
                }

                configPath = value;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return new ParsedOptions($"无法识别的参数: '{token}'。");
            }

            var optionName = token[2..];
            if (!optionMap.TryGetValue(optionName, out var parameter))
            {
                return new ParsedOptions($"未知选项: '{token}'。");
            }

            if (values.ContainsKey(optionName))
            {
                return new ParsedOptions($"选项 '{token}' 重复出现。");
            }

            if (parameter.IsBoolean)
            {
                if (TryPeekOptionValue(args, i, out var boolValue))
                {
                    i++;
                    values[optionName] = boolValue;
                }
                else
                {
                    values[optionName] = "true";
                }

                continue;
            }

            if (!TryReadOptionValue(args, ref i, out var rawValue))
            {
                return new ParsedOptions($"选项 '{token}' 缺少值。");
            }

            values[optionName] = rawValue;
        }

        return new ParsedOptions(null, values, configPath, yesAccepted, helpRequested);
    }

    private static bool TryReadOptionValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryPeekOptionValue(IReadOnlyList<string> args, int index, out string? value)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        value = args[index + 1];
        return true;
    }

    private static bool IsHelpToken(string token)
    {
        return string.Equals(token, "--help", StringComparison.Ordinal) ||
               string.Equals(token, "-h", StringComparison.Ordinal);
    }

    private sealed record ParsedOptions(
        string? ErrorMessage,
        IReadOnlyDictionary<string, string?>? OptionValues = null,
        string? ConfigPath = null,
        bool YesAccepted = false,
        bool HelpRequested = false);
}
