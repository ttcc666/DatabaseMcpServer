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
    }
}
