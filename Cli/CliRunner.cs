using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Cli;

internal sealed class CliRunner
{
    private const int SuccessExitCode = 0;
    private const int ToolFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;

    private readonly CliToolCatalog _catalog;
    private readonly CliCommandParser _parser;

    public CliRunner()
        : this(new CliToolCatalog())
    {
    }

    internal CliRunner(CliToolCatalog catalog)
    {
        _catalog = catalog;
        _parser = new CliCommandParser(catalog);
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var parseResult = _parser.Parse(args);

        switch (parseResult.Kind)
        {
            case CliCommandKind.ToolRootHelp:
                await WriteToolRootHelpAsync(stderr);
                return parseResult.ErrorMessage == null ? SuccessExitCode : UsageErrorExitCode;

            case CliCommandKind.ToolList:
                await WriteToolListAsync(stderr);
                return SuccessExitCode;

            case CliCommandKind.ToolHelp:
                await WriteToolHelpAsync(parseResult.Tool!, stderr);
                return SuccessExitCode;

            case CliCommandKind.Error:
                await stderr.WriteLineAsync(parseResult.ErrorMessage);
                await WriteToolRootHelpAsync(stderr);
                return UsageErrorExitCode;

            case CliCommandKind.ToolInvoke:
                return await ExecuteToolAsync(parseResult, stdout, stderr);

            default:
                await stderr.WriteLineAsync("未知 CLI 状态。");
                return UsageErrorExitCode;
        }
    }

    public static Task WriteRootHelpAsync(TextWriter stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  DatabaseMcpServer");
        builder.AppendLine("  DatabaseMcpServer tool list");
        builder.AppendLine("  DatabaseMcpServer tool help <tool_name>");
        builder.AppendLine("  DatabaseMcpServer tool <tool_name> [--option value...] [--config path] [--yes]");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("  No arguments starts the stdio MCP server.");
        builder.AppendLine("  CLI help and metadata are written to stderr.");
        builder.AppendLine("  Tool JSON results are written to stdout.");
        builder.AppendLine();
        builder.AppendLine("Examples:");
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

            var builder = DatabaseHostBuilderFactory.CreateBaseBuilder([], silentLogs: true);
            using var host = builder.Build();
            var toolInstance = host.Services.GetRequiredService(tool.ToolType);
            var payload = await InvokeToolAsync(tool, toolInstance, arguments);

            await stdout.WriteAsync(payload);
            return IsToolFailure(payload) ? ToolFailureExitCode : SuccessExitCode;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync(Unwrap(ex).Message);
            return UsageErrorExitCode;
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
            Console.SetError(originalConsoleError);
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
        }
    }

    private static async Task<string> InvokeToolAsync(CliToolMetadata tool, object toolInstance, object[] arguments)
    {
        var result = tool.Method.Invoke(toolInstance, arguments);
        return result switch
        {
            string text => text,
            Task<string> task => await task,
            _ => throw new InvalidOperationException($"tool '{tool.Name}' 返回类型不受支持。")
        };
    }

    internal static object[] BindArguments(CliToolMetadata tool, IReadOnlyDictionary<string, string?> optionValues)
    {
        var values = new object[tool.Parameters.Count];
        for (var i = 0; i < tool.Parameters.Count; i++)
        {
            var parameter = tool.Parameters[i];
            if (optionValues.TryGetValue(parameter.OptionName, out var rawValue))
            {
                values[i] = ConvertValue(parameter, rawValue);
                continue;
            }

            if (parameter.IsRequired)
            {
                throw new InvalidOperationException($"缺少必填选项 '--{parameter.OptionName}'。");
            }

            values[i] = parameter.DefaultValue!;
        }

        return values;
    }

    private static object ConvertValue(CliToolParameterMetadata parameter, string? rawValue)
    {
        var targetType = parameter.EffectiveType;

        if (targetType == typeof(string))
        {
            return rawValue ?? string.Empty;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return intValue;
            }

            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要 int 值。");
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(rawValue, out var boolValue))
            {
                return boolValue;
            }

            throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要 bool 值。");
        }

        if (targetType == typeof(JsonElement))
        {
            try
            {
                return JsonDocument.Parse(rawValue ?? "null").RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 需要有效 JSON。{ex.Message}");
            }
        }

        throw new InvalidOperationException($"选项 '--{parameter.OptionName}' 使用了不支持的参数类型 '{targetType.Name}'。");
    }

    private static bool IsToolFailure(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("success", out var successElement) &&
                   successElement.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null }
            ? exception.InnerException
            : exception;
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
