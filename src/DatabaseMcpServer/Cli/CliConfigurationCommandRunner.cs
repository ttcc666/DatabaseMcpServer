using DatabaseMcpServer.Configuration;
using static DatabaseMcpServer.Cli.CliCommandOptions;

namespace DatabaseMcpServer.Cli;

/// <summary>Maps parsed init/config commands to configuration operations and writes their results.</summary>
internal sealed class CliConfigurationCommandRunner(
    ConfigurationCommands configCommandHandler, string? currentDatabaseStateFilePath)
{
    private const int SuccessExitCode = 0;
    private const int ToolFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;

    public async Task<int> RunAsync(CliParseResult parseResult, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        switch (parseResult.Kind)
        {
            case CliCommandKind.InitInvoke:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Initialize(
                        parseResult.ConfigPath,
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                    stdout);

            case CliCommandKind.ConfigList:
                return await ExecuteConfigPayloadAsync(configCommandHandler.List(parseResult.ConfigPath), stdout);

            case CliCommandKind.ConfigShow:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Show(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                    stdout);

            case CliCommandKind.ConfigPresets:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.ListPresets(),
                    stdout);

            case CliCommandKind.ConfigPreset:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.ShowPreset(GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "db-type")),
                    stdout);

            case CliCommandKind.ConfigCreate:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.CreateFromPreset(
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
                    configCommandHandler.Add(
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
                    configCommandHandler.Rename(
                        parseResult.ConfigPath,
                        GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"),
                        GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "new-name"),
                        currentDatabaseStateFilePath),
                    stdout);

            case CliCommandKind.ConfigUpdate:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Update(
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
                    configCommandHandler.Clone(
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
                    configCommandHandler.Remove(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                    stdout);

            case CliCommandKind.ConfigSetDefault:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.SetDefault(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                    stdout);

            case CliCommandKind.ConfigUse:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Use(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name")),
                    stdout);

            case CliCommandKind.ConfigTest:
                return await ExecuteConfigPayloadAsync(
                    await configCommandHandler.TestAsync(parseResult.ConfigPath, GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "name"), cancellationToken),
                    stdout);

            case CliCommandKind.ConfigValidate:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Validate(parseResult.ConfigPath),
                    stdout);

            case CliCommandKind.ConfigDoctor:
                return await ExecuteConfigPayloadAsync(
                    await configCommandHandler.DoctorAsync(
                        parseResult.ConfigPath,
                        GetOptionalStringOption(parseResult.OptionValues, "name"),
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "test-connections"),
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "fix-suggestions"),
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "summary-only"), cancellationToken),
                    stdout);

            case CliCommandKind.ConfigExport:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Export(
                        parseResult.ConfigPath,
                        GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "output"),
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                    stdout);

            case CliCommandKind.ConfigImport:
                return await ExecuteConfigPayloadAsync(
                    configCommandHandler.Import(
                        parseResult.ConfigPath,
                        GetRequiredStringOption(parseResult.Command!, parseResult.OptionValues, "input"),
                        GetBoolOption(parseResult.Command!, parseResult.OptionValues, "force")),
                    stdout);

            default:
                throw new InvalidOperationException("未知配置命令。");
        }
    }

    private static async Task<int> ExecuteConfigPayloadAsync(DatabaseMcpServer.Models.ConfigurationCommandResult result, TextWriter stdout)
    {
        await stdout.WriteAsync(System.Text.Json.JsonSerializer.Serialize(result, DatabaseMcpServer.Helpers.JsonSerializationDefaults.IndentedCamelCase));
        return result.Success ? SuccessExitCode : ToolFailureExitCode;
    }
}
