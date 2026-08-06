using System.Reflection;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Extensions;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tests;

public class CliToolCatalogTests
{
    [Fact]
    public void ToolCatalog_ShouldExposeAllMcpTools_WithSnakeCaseAndKebabCaseNames()
    {
        var catalog = new CliToolCatalog();

        var expectedToolCount = DatabaseMcpToolCatalog.ToolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Count(method => method.GetCustomAttribute<McpServerToolAttribute>() != null);

        Assert.Equal(expectedToolCount, catalog.Tools.Count);
        Assert.All(catalog.Tools, tool => Assert.Matches("^[a-z0-9_]+$", tool.Name));
        Assert.All(catalog.Tools.SelectMany(tool => tool.Parameters), parameter => Assert.Matches("^[a-z0-9-]+$", parameter.OptionName));

        var switchDatabase = Assert.Single(catalog.Tools, tool => tool.Name == "switch_database");
        var databaseNameParameter = Assert.Single(switchDatabase.Parameters);
        Assert.Equal("database-name", databaseNameParameter.OptionName);

        var executeCommand = Assert.Single(catalog.Tools, tool => tool.Name == "execute_command");
        var timeoutParameter = Assert.Single(executeCommand.Parameters, parameter => parameter.ParameterName == "commandTimeoutSeconds");
        Assert.Equal("command-timeout-seconds", timeoutParameter.OptionName);
        Assert.False(timeoutParameter.IsRequired);
        Assert.Equal(typeof(int?), timeoutParameter.ParameterType);
        Assert.Equal("int", timeoutParameter.DisplayTypeName);

        var sqlQuery = Assert.Single(catalog.Tools, tool => tool.Name == "sql_query");
        Assert.Contains(sqlQuery.Parameters, parameter => parameter.OptionName == "command-timeout-seconds");
    }
}
