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
        var result = _parser.Parse(["switch_database", "--help"]);

        Assert.Equal(CliCommandKind.ToolHelp, result.Kind);
        Assert.Equal("switch_database", result.Tool?.Name);
    }

    [Fact]
    public void Parse_ShouldRejectUnknownOption()
    {
        var result = _parser.Parse(["switch_database", "--unknown-option", "value"]);

        Assert.Equal(CliCommandKind.Error, result.Kind);
        Assert.Contains("未知选项", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ShouldSuggestClosestToolNames_ForUnknownTool()
    {
        var result = _parser.Parse(["list_database"]);

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
