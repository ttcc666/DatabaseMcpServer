using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;

namespace DatabaseMcpServer.Tests;

public class ToolJsonBindingTests
{
    [Fact]
    public void JsonArguments_PreserveTypedValuesAndDefaultsWithoutCliConversion()
    {
        Assert.True(new ToolCatalog().TryGetTool("query_database", out var tool));
        var arguments = ToolInvoker.BindJsonArguments(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
            {"sql":"select @value", "max-rows":2, "parameters":[{"name":"value","value":9007199254740993,"dbType":"Int64"}]}
            """)!);
        object? Value(string name) => arguments[tool.Parameters.ToList().FindIndex(p => p.ParameterName == name)];
        Assert.Equal("select @value", Value("sql"));
        Assert.Equal(2, Value("maxRows"));
        Assert.Null(Value("databaseName"));
        var parameter = Assert.Single(Assert.IsType<DatabaseParameter[]>(Value("parameters")));
        Assert.Equal(9007199254740993L, parameter.Value.GetInt64());
    }

    [Theory]
    [InlineData("{\"sql\":\"select 1\",\"max-rows\":\"2\"}")]
    [InlineData("{\"sql\":\"select 1\",\"max-rows\":2.5}")]
    [InlineData("{\"sql\":\"select 1\",\"unknown\":1}")]
    [InlineData("{\"max-rows\":2}")]
    [InlineData("{\"sql\":\"select 1\",\"parameters\":\"[]\"}")]
    public void JsonArguments_RejectWrongTypesUnknownKeysAndMissingRequiredValues(string json)
    {
        Assert.True(new ToolCatalog().TryGetTool("query_database", out var tool));
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(tool,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!));
    }

    [Fact]
    public void JsonArguments_KeepExtendedJsonDocumentsAfterSourceDisposed()
    {
        Assert.True(new ToolCatalog().TryGetTool("mongo_find", out var tool));
        object[] arguments;
        using (var source = JsonDocument.Parse("""{"collection":"docs","filter":{"id":{"$numberLong":"9007199254740993"}}} """))
            arguments = ToolInvoker.BindJsonArguments(tool, source.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value));
        var filter = Assert.IsType<Dictionary<string, JsonElement>>(arguments[tool.Parameters.ToList().FindIndex(p => p.ParameterName == "filter")]);
        Assert.Equal("9007199254740993", filter["id"].GetProperty("$numberLong").GetString());
    }
}
