using System.Text.Json;
using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class CliCommandParserTests
{
    private readonly CliToolCatalog _catalog = new();
    private readonly CliCommandParser _parser;

    public CliCommandParserTests()
    {
        _parser = new CliCommandParser(_catalog);
    }

    [Fact]
    public void Parse_ShouldReturnToolHelp_WhenHelpFlagIsPresent()
    {
        var result = _parser.Parse(["tool", "switch_database", "--help"]);

        Assert.Equal(CliCommandKind.ToolHelp, result.Kind);
        Assert.Equal("switch_database", result.Tool?.Name);
    }

    [Fact]
    public void Parse_ShouldReturnConfigHelp_WhenHelpFlagIsPresent()
    {
        var result = _parser.Parse(["config", "add", "--help"]);

        Assert.Equal(CliCommandKind.ConfigHelp, result.Kind);
        Assert.Equal("config add", result.Command?.Name);
    }

    [Fact]
    public void Parse_ShouldParseWebCommand()
    {
        var result = _parser.Parse(["-web", "--port", "4317", "--no-browser"]);

        Assert.Equal(CliCommandKind.WebInvoke, result.Kind);
        Assert.Equal("-web", result.Command?.Name);
        Assert.Equal("4317", result.OptionValues?["port"]);
        Assert.Equal("true", result.OptionValues?["no-browser"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigPresetsCommand()
    {
        var result = _parser.Parse(["config", "presets"]);

        Assert.Equal(CliCommandKind.ConfigPresets, result.Kind);
    }

    [Fact]
    public void Parse_ShouldParseConfigPresetCommand()
    {
        var result = _parser.Parse(["config", "preset", "--db-type", "Sqlite"]);

        Assert.Equal(CliCommandKind.ConfigPreset, result.Kind);
        Assert.Equal("Sqlite", result.OptionValues?["db-type"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigCreateCommand()
    {
        var result = _parser.Parse(["config", "create", "--from-preset", "Sqlite", "--name", "sqlite-dev", "--connection-string", "Data Source=dev.db;", "--description", "dev", "--set-default", "--print-only"]);

        Assert.Equal(CliCommandKind.ConfigCreate, result.Kind);
        Assert.Equal("Sqlite", result.OptionValues?["from-preset"]);
        Assert.Equal("sqlite-dev", result.OptionValues?["name"]);
        Assert.Equal("Data Source=dev.db;", result.OptionValues?["connection-string"]);
        Assert.Equal("dev", result.OptionValues?["description"]);
        Assert.Equal("true", result.OptionValues?["set-default"]);
        Assert.Equal("true", result.OptionValues?["print-only"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigUpdateWithClearDescription()
    {
        var result = _parser.Parse(["config", "update", "--name", "sqlite-dev", "--clear-description"]);

        Assert.Equal(CliCommandKind.ConfigUpdate, result.Kind);
        Assert.Equal("sqlite-dev", result.OptionValues?["name"]);
        Assert.Equal("true", result.OptionValues?["clear-description"]);
    }

    [Fact]
    public void Parse_ShouldParseInitCommand()
    {
        var result = _parser.Parse(["init", "--force"]);

        Assert.Equal(CliCommandKind.InitInvoke, result.Kind);
        Assert.Equal("true", result.OptionValues?["force"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigAddCommand()
    {
        var result = _parser.Parse([
            "config",
            "add",
            "--name", "sqlite-local",
            "--db-type", "Sqlite",
            "--connection-string", "Data Source=test.db;",
            "--set-default"
        ]);

        Assert.Equal(CliCommandKind.ConfigAdd, result.Kind);
        Assert.Equal("sqlite-local", result.OptionValues?["name"]);
        Assert.Equal("Sqlite", result.OptionValues?["db-type"]);
        Assert.Equal("Data Source=test.db;", result.OptionValues?["connection-string"]);
        Assert.Equal("true", result.OptionValues?["set-default"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigExportCommand()
    {
        var result = _parser.Parse(["config", "export", "--output", ".\\backup.json", "--force"]);

        Assert.Equal(CliCommandKind.ConfigExport, result.Kind);
        Assert.Equal(".\\backup.json", result.OptionValues?["output"]);
        Assert.Equal("true", result.OptionValues?["force"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigImportCommand()
    {
        var result = _parser.Parse(["config", "import", "--input", ".\\import.json"]);

        Assert.Equal(CliCommandKind.ConfigImport, result.Kind);
        Assert.Equal(".\\import.json", result.OptionValues?["input"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigUseCommand()
    {
        var result = _parser.Parse(["config", "use", "--name", "sqlite-local"]);

        Assert.Equal(CliCommandKind.ConfigUse, result.Kind);
        Assert.Equal("sqlite-local", result.OptionValues?["name"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigRenameCommand()
    {
        var result = _parser.Parse(["config", "rename", "--name", "sqlite-local", "--new-name", "sqlite-dev"]);

        Assert.Equal(CliCommandKind.ConfigRename, result.Kind);
        Assert.Equal("sqlite-local", result.OptionValues?["name"]);
        Assert.Equal("sqlite-dev", result.OptionValues?["new-name"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigUpdateCommand()
    {
        var result = _parser.Parse(["config", "update", "--name", "sqlite-local", "--db-type", "SqlServer", "--set-default"]);

        Assert.Equal(CliCommandKind.ConfigUpdate, result.Kind);
        Assert.Equal("sqlite-local", result.OptionValues?["name"]);
        Assert.Equal("SqlServer", result.OptionValues?["db-type"]);
        Assert.Equal("true", result.OptionValues?["set-default"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigValidateCommand()
    {
        var result = _parser.Parse(["config", "validate"]);

        Assert.Equal(CliCommandKind.ConfigValidate, result.Kind);
    }

    [Fact]
    public void Parse_ShouldParseConfigCloneCommand()
    {
        var result = _parser.Parse(["config", "clone", "--name", "sqlite-dev", "--new-name", "sqlite-ci", "--set-default"]);

        Assert.Equal(CliCommandKind.ConfigClone, result.Kind);
        Assert.Equal("sqlite-dev", result.OptionValues?["name"]);
        Assert.Equal("sqlite-ci", result.OptionValues?["new-name"]);
        Assert.Equal("true", result.OptionValues?["set-default"]);
    }

    [Fact]
    public void Parse_ShouldParseConfigDoctorCommand()
    {
        var result = _parser.Parse(["config", "doctor", "--name", "sqlite-dev", "--test-connections", "false", "--fix-suggestions", "true", "--summary-only"]);

        Assert.Equal(CliCommandKind.ConfigDoctor, result.Kind);
        Assert.Equal("sqlite-dev", result.OptionValues?["name"]);
        Assert.Equal("false", result.OptionValues?["test-connections"]);
        Assert.Equal("true", result.OptionValues?["fix-suggestions"]);
        Assert.Equal("true", result.OptionValues?["summary-only"]);
    }

    [Fact]
    public void Parse_ShouldRejectUnknownOption()
    {
        var result = _parser.Parse(["tool", "switch_database", "--unknown-option", "value"]);

        Assert.Equal(CliCommandKind.Error, result.Kind);
        Assert.Contains("未知选项", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ShouldSuggestClosestToolNames_ForUnknownTool()
    {
        var result = _parser.Parse(["tool", "list_database"]);

        Assert.Equal(CliCommandKind.Error, result.Kind);
        Assert.Contains("list_databases", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BindArguments_ShouldHandleIntBoolJsonAndNullableDefaults()
    {
        var retryTool = GetTool("test_connection_with_retry");
        var retryArguments = CliRunner.BindArguments(
            retryTool,
            new Dictionary<string, string?>
            {
                ["max-retries"] = "5",
                ["initial-delay-ms"] = "250"
            });

        Assert.Equal(5, retryArguments[0]);
        Assert.Equal(250, retryArguments[1]);

        var createIndexTool = GetTool("create_index");
        var createIndexArguments = CliRunner.BindArguments(
            createIndexTool,
            new Dictionary<string, string?>
            {
                ["table-name"] = "users",
                ["index-name"] = "ix_users_name",
                ["column-name"] = "name",
                ["is-unique"] = "true"
            });

        Assert.Equal("users", createIndexArguments[0]);
        Assert.Equal("ix_users_name", createIndexArguments[1]);
        Assert.Equal("name", createIndexArguments[2]);
        Assert.Equal(true, createIndexArguments[3]);

        var createTableTool = GetTool("create_table");
        Assert.True(createTableTool.RequiresConfirmation);
        var createTableArguments = CliRunner.BindArguments(
            createTableTool,
            new Dictionary<string, string?>
            {
                ["table-name"] = "users",
                ["columns-info"] = "[]",
                ["is-create-primary-key"] = "false"
            });

        Assert.Equal("users", createTableArguments[0]);
        Assert.Equal("[]", createTableArguments[1]);
        Assert.Equal(false, createTableArguments[2]);
        var batchTool = GetTool("batch_execute_commands");
        var batchArguments = CliRunner.BindArguments(
            batchTool,
            new Dictionary<string, string?>
            {
                ["commands"] = "[\"SELECT 1\"]"
            });

        Assert.IsType<JsonElement>(batchArguments[0]);
        Assert.Equal(JsonValueKind.Array, ((JsonElement)batchArguments[0]).ValueKind);
        Assert.Null(batchArguments[1]);

        var batchQueryTool = GetTool("batch_sql_query");
        var batchQueryArguments = CliRunner.BindArguments(
            batchQueryTool,
            new Dictionary<string, string?>
            {
                ["queries"] = "[\"SELECT 1\",\"SELECT 2\"]"
            });

        Assert.IsType<JsonElement>(batchQueryArguments[0]);
        Assert.Equal(JsonValueKind.Array, ((JsonElement)batchQueryArguments[0]).ValueKind);
        Assert.False(batchQueryTool.RequiresConfirmation);

        var sqlQueryTool = GetTool("sql_query");
        var sqlQueryArguments = CliRunner.BindArguments(
            sqlQueryTool,
            new Dictionary<string, string?>
            {
                ["sql"] = "select 1"
            });

        Assert.Equal("select 1", sqlQueryArguments[0]);
        Assert.Null(sqlQueryArguments[1]);
    }

    [Fact]
    public void BindArguments_ShouldThrow_WhenRequiredOptionIsMissing()
    {
        var tool = GetTool("switch_database");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CliRunner.BindArguments(tool, new Dictionary<string, string?>()));

        Assert.Contains("--database-name", exception.Message, StringComparison.Ordinal);
    }

    private CliToolMetadata GetTool(string toolName)
    {
        Assert.True(_catalog.TryGetTool(toolName, out var tool));
        return tool;
    }
}
