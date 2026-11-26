using System.Globalization;
using System.Text;
using DatabaseMcpServer.Tools;

namespace DatabaseMcpServer.Cli;

/// <summary>Formats CLI help and tool metadata without creating an application host.</summary>
internal static class CliHelpWriter
{
    public static Task WriteRootHelpAsync(TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  DatabaseMcpServer --version");
        builder.AppendLine("  DatabaseMcpServer export --sql <select> --output <path> [--format csv|jsonl] [--overwrite]");
        builder.AppendLine("  DatabaseMcpServer [--enable-monitor-config true|false]");
        builder.AppendLine("  DatabaseMcpServer -web [--config path] [--port 0-65535] [--no-browser]");
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

    public static Task WriteToolListAsync(TextWriter stderr, IEnumerable<ToolDescriptor> tools)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Available tools:");
        foreach (var tool in tools)
        {
            builder.AppendLine($"  {tool.Name} - {tool.Description}");
        }

        return stderr.WriteAsync(builder.ToString());
    }

    public static Task WriteToolRootHelpAsync(TextWriter stderr)
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

    public static Task WriteToolHelpAsync(ToolDescriptor tool, TextWriter stderr)
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

    public static Task WriteCommandHelpAsync(CliCommandMetadata command, TextWriter stderr)
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
}
