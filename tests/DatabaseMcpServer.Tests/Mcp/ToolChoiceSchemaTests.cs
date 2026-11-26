using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ToolChoiceSchemaTests
{
    [Fact]
    public void Discovery_UsesEnumsFromCapabilityDeclarations()
    {
        var catalog = new ToolCatalog();
        var sections = catalog.Tools.Single(t => t.Name == "get_table_schema").ProtocolTool.InputSchema
            .GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("enum");
        Assert.Equal(CapabilityNames<SchemaSectionKind>.All, sections.EnumerateArray().Select(v => v.GetString()));
    }

    [Theory]
    [InlineData("get_table_schema", "sections", "[\"Columns\"]")]
    [InlineData("get_table_schema", "sections", "[null]")]
    public async Task InvalidChoice_RejectsCliJsonAndWebBeforeInvocation(string name, string parameter, string raw)
    {
        var tool = new ToolCatalog().Tools.Single(t => t.Name == name);
        var value = JsonSerializer.Deserialize<JsonElement>(raw);
        var json = new Dictionary<string, JsonElement> { [parameter] = value };
        var cli = new Dictionary<string, string?> { [parameter] = value.ValueKind == JsonValueKind.String ? value.GetString() : raw };
        Assert.Contains("仅接受", Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindArguments(tool, cli)).Message);
        Assert.Contains("仅接受", Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(tool, json)).Message);
        using var fixture = DatabaseRoleTests.Create();
        var web = fixture.Host.Services.GetRequiredService<CliWebToolService>();
        Assert.Contains("仅接受", (await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync(name, new(json, null), default))).Message);
    }
}
