namespace DatabaseMcpServer.Cli;

internal enum CliCommandKind
{
    ToolRootHelp,
    ToolList,
    ToolHelp,
    ToolInvoke,
    Error
}

internal sealed record CliParseResult(
    CliCommandKind Kind,
    CliToolMetadata? Tool = null,
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
            return new CliParseResult(CliCommandKind.ToolRootHelp, ErrorMessage: "缺少 tool 名称。");
        }

        if (IsHelpToken(args[0]))
        {
            return new CliParseResult(CliCommandKind.ToolRootHelp);
        }

        if (string.Equals(args[0], "list", StringComparison.Ordinal))
        {
            return ParseNoToolCommand(args.Skip(1).ToArray(), CliCommandKind.ToolList);
        }

        if (string.Equals(args[0], "help", StringComparison.Ordinal))
        {
            return ParseHelpCommand(args.Skip(1).ToArray());
        }

        if (!_catalog.TryGetTool(args[0], out var tool))
        {
            var suggestions = _catalog.GetClosestToolNames(args[0]);
            var suggestionText = suggestions.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}最接近的命令: {string.Join(", ", suggestions)}";
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: $"未知 tool: '{args[0]}'。{suggestionText}");
        }

        var parsedOptions = ParseOptions(args.Skip(1).ToArray(), tool.Parameters);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: parsedOptions.ErrorMessage);
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

    private CliParseResult ParseNoToolCommand(IReadOnlyList<string> args, CliCommandKind kind)
    {
        var parsedOptions = ParseOptions(args, []);
        if (parsedOptions.ErrorMessage != null)
        {
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: parsedOptions.ErrorMessage);
        }

        return new CliParseResult(kind, ConfigPath: parsedOptions.ConfigPath, ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private CliParseResult ParseHelpCommand(IReadOnlyList<string> args)
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
            return new CliParseResult(CliCommandKind.Error, ErrorMessage: parsedOptions.ErrorMessage);
        }

        return new CliParseResult(
            CliCommandKind.ToolHelp,
            Tool: tool,
            ConfigPath: parsedOptions.ConfigPath,
            ConfirmationAccepted: parsedOptions.YesAccepted);
    }

    private static ParsedOptions ParseOptions(
        IReadOnlyList<string> args,
        IReadOnlyList<CliToolParameterMetadata> parameters)
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
