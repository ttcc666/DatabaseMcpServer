using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class CapabilityDeclarationTests
{
    [Fact]
    public async Task FullDiscovery_ExportsCompleteRulesAndLiveCapabilityDetails()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await new CliRunner().RunAsync(["tool", "list", "--json", "--all", "--config", fixture.Path], output, error));
        var catalog = JsonSerializer.Deserialize<JsonElement>(output.ToString());
        Assert.Equal(ToolContract.Names, catalog.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Order());
        var declarations = catalog.GetProperty("capabilityCatalog");
        Assert.Equal(Enum.GetNames<DatabaseCapability>(), declarations.GetProperty("capabilities").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        foreach (var rule in declarations.GetProperty("capabilities").EnumerateArray())
            Assert.True((rule.GetProperty("supportedProviders").GetArrayLength() > 0) ^ (rule.GetProperty("unsupportedProviders").GetArrayLength() > 0), rule.ToString());
        Assert.Equal(new[] { "columns", "primaryKeys", "identityColumns", "indexes", "foreignKeys", "constraints", "comments" },
            declarations.GetProperty("schemaSections").EnumerateArray().Select(v => v.GetString()));
        var sqlite = declarations.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Sqlite").GetProperty("details");
        var actual = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities()).GetProperty("capabilityDetails");
        Assert.True(JsonElement.DeepEquals(sqlite, actual));
    }

    [Fact]
    public async Task DefaultSectionsAndSdkHelp_UseDeclaredNames()
    {
        using var fixture = DatabaseRoleTests.Create();
        var data = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<SchemaTools>().GetTableSchema("markers")).GetProperty("data");
        Assert.Equal(new[] { "columns", "identityColumns", "indexes", "primaryKeys", "tableName" }, data.EnumerateObject().Select(p => p.Name).Order());
        var catalog = new ToolCatalog();
        var schemaHelp = catalog.Tools.Single(t => t.Name == "get_table_schema").ProtocolTool.InputSchema.GetProperty("properties").GetProperty("sections").GetProperty("description").GetString()!;
        foreach (var name in CapabilityNames<SchemaSectionKind>.All) Assert.Contains(name, schemaHelp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Columns")]
    [InlineData("unknown")]
    public async Task InvalidDiscriminators_FailValidationWithoutFallback(string value)
    {
        using var fixture = DatabaseRoleTests.Create();
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<SchemaTools>().GetTableSchema("markers", sections: [value]), 1004);
    }

    [Fact]
    public async Task EmptyAndRepeatedSections_AreValidatedAndDeduplicated()
    {
        using var fixture = DatabaseRoleTests.Create();
        var tool = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Error(await tool.GetTableSchema("markers", sections: []), 1004);
        var data = DatabaseRoleTests.Success(await tool.GetTableSchema("markers", sections: ["columns", "columns"])).GetProperty("data");
        Assert.Equal(new[] { "columns", "tableName" }, data.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("value", data.GetProperty("columns")[0].GetProperty("name").GetString());
    }
}
