using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;

namespace DatabaseMcpServer.Tests;

public class ToolJsonBindingTests
{
    [Fact]
    public void JsonNulls_FollowDeclaredNullabilityAndPreserveDefaults()
    {
        Assert.True(new ToolCatalog().TryGetTool("query_database", out var tool));
        var arguments = ToolInvoker.BindJsonArguments(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"sql":"select 1","database-name":null,"max-rows":null,"parameters":null}""")!);
        Assert.Null(arguments[tool.Parameters.ToList().FindIndex(p => p.ParameterName == "maxRows")]);
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(tool,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"sql":null}""")!));
        Assert.True(new ToolCatalog().TryGetTool("mongo_find", out var mongo));
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(mongo,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"collection":"docs","filter":null}""")!));
    }

    [Theory]
    [InlineData("[{\"sql\":\"select 1\",\"SQL\":\"delete from items\"}]")]
    [InlineData("[{\"sql\":\"select @p\",\"parameters\":[{\"name\":\"p\",\"value\":1,\"Value\":2}]}]")]
    public void TypedArrays_RejectDuplicatePropertiesInCliAndWeb(string commands)
    {
        Assert.True(new ToolCatalog().TryGetTool("batch_sql_query", out var tool));
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindArguments(tool, new Dictionary<string, string?> { ["commands"] = commands }));
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(tool,
            new Dictionary<string, JsonElement> { ["commands"] = JsonSerializer.Deserialize<JsonElement>(commands) }));
    }

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
