namespace DatabaseMcpServer.Cli;

internal sealed record DatabaseStartupArguments(
    bool? EnableMonitorConfig,
    IReadOnlyList<string> RemainingArgs,
    string? ErrorMessage)
{
    public const string EnableMonitorConfigOptionName = "enable-monitor-config";

    public bool RunMcpServer => ErrorMessage == null && RemainingArgs.Count == 0;

    public static DatabaseStartupArguments Parse(IReadOnlyList<string> args)
    {
        bool? enableMonitorConfig = null;
        var index = 0;

        while (index < args.Count)
        {
            if (!TryConsumeEnableMonitorConfig(args, ref index, out var enabled, out var errorMessage))
            {
                break;
            }

            if (errorMessage != null)
            {
                return new DatabaseStartupArguments(null, args, errorMessage);
            }

            enableMonitorConfig = enabled;
        }

        return new DatabaseStartupArguments(
            enableMonitorConfig,
            args.Skip(index).ToArray(),
            null);
    }

    private static bool TryConsumeEnableMonitorConfig(
        IReadOnlyList<string> args,
        ref int index,
        out bool enabled,
        out string? errorMessage)
    {
        enabled = false;
        errorMessage = null;
        var token = args[index];
        var prefix = $"--{EnableMonitorConfigOptionName}";

        if (token.StartsWith(prefix + "=", StringComparison.Ordinal))
        {
            var rawValue = token[(prefix.Length + 1)..];
            if (!bool.TryParse(rawValue, out enabled))
            {
                errorMessage = $"选项 '{prefix}' 需要 bool 值。";
            }

            index++;
            return true;
        }

        if (!string.Equals(token, prefix, StringComparison.Ordinal))
        {
            return false;
        }

        index++;
        if (index < args.Count && bool.TryParse(args[index], out var parsed))
        {
            enabled = parsed;
            index++;
            return true;
        }

        enabled = true;
        return true;
    }
}
