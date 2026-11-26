using System.Globalization;

namespace DatabaseMcpServer.Cli;

/// <summary>Reads builtin command options using their declared defaults and existing CLI validation rules.</summary>
internal static class CliCommandOptions
{
    public static bool GetBoolOption(
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

    public static int? GetOptionalIntOption(
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

    public static string GetRequiredStringOption(
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

    public static string? GetOptionalStringOption(IReadOnlyDictionary<string, string?>? optionValues, string optionName)
    {
        return optionValues != null && optionValues.TryGetValue(optionName, out var rawValue)
            ? rawValue
            : null;
    }

    public static bool HasOption(IReadOnlyDictionary<string, string?>? optionValues, string optionName)
    {
        return optionValues != null && optionValues.ContainsKey(optionName);
    }
}
