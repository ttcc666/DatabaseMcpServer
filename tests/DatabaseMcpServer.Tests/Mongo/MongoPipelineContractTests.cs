using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;

namespace DatabaseMcpServer.Tests;

public class MongoPipelineContractTests
{
    [Fact]
    public void Pipeline_AcceptsLookupFacetAndEnforcesAggregateStageCount()
    {
        var bytes = 0;
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>[]>("""
            [{"$lookup":{"from":"customers","let":{"customer":"$customerId"},"pipeline":[{"$match":{"$expr":{"$eq":["$_id","$$customer"]}}},{"$limit":10}],"as":"customer"}},
             {"$facet":{"rows":[{"$limit":10}],"total":[{"$count":"value"}]}}]
            """);
        Assert.Equal(2, MongoPipelineContract.Parse(input, ref bytes).Length);
        var branches = new Dictionary<string, object> { ["rows"] = Enumerable.Range(0, 64).Select(_ => new Dictionary<string, int> { ["$limit"] = 1 }).ToArray() };
        var excessive = JsonSerializer.Deserialize<Dictionary<string, JsonElement>[]>(JsonSerializer.Serialize(new[] { new Dictionary<string, object> { ["$facet"] = branches } }));
        bytes = 0;
        Assert.Throws<DatabaseMcpException>(() => MongoPipelineContract.Parse(excessive, ref bytes));
    }

    [Theory]
    [InlineData("[{\"$lookup\":{\"from\":{\"db\":\"other\",\"coll\":\"docs\"},\"pipeline\":[],\"as\":\"x\"}}]")]
    [InlineData("[{\"$lookup\":{\"from\":\"system.users\",\"pipeline\":[],\"as\":\"x\"}}]")]
    [InlineData("[{\"$facet\":{\"x\":[{\"$lookup\":{\"from\":\"docs\",\"pipeline\":[{\"$facet\":{\"y\":[]}}],\"as\":\"x\"}}]}}]")]
    [InlineData("[{\"$lookup\":{\"from\":\"docs\",\"pipeline\":[{\"$unionWith\":\"other\"}],\"as\":\"x\"}}]")]
    public void Pipeline_RejectsCrossDatabaseSystemCollectionsAndNestedUnsupportedStages(string json)
    {
        var bytes = 0;
        var pipeline = JsonSerializer.Deserialize<Dictionary<string, JsonElement>[]>(json);
        Assert.Throws<DatabaseMcpException>(() => MongoPipelineContract.Parse(pipeline, ref bytes));
    }

    [Theory]
    [InlineData("[{\"$out\":\"copy\"}]")]
    [InlineData("[{\"$merge\":\"copy\"}]")]
    [InlineData("[{\"$lookup\":{\"from\":\"docs\",\"pipeline\":[{\"$out\":\"copy\"}],\"as\":\"x\"}}]")]
    [InlineData("[{\"$facet\":{\"x\":[{\"$merge\":\"copy\"}]}}]")]
    [InlineData("[{\"$project\":{\"x\":[{\"$function\":{\"body\":\"evil\"}}]}}]")]
    [InlineData("[{\"$group\":{\"_id\":null,\"x\":{\"$accumulator\":{}}}}]")]
    [InlineData("[{\"$match\":{\"$where\":\"evil\"}}]")]
    [InlineData("[{\"$limit\":1,\"$skip\":1}]")]
    [InlineData("[]")]
    public void Pipeline_RejectsWritesAndScriptsAtEveryDepth(string json)
    {
        var pipeline = JsonSerializer.Deserialize<Dictionary<string, JsonElement>[]>(json);
        var bytes = 0;
        var ex = Assert.Throws<DatabaseMcpException>(() => MongoPipelineContract.Parse(pipeline, ref bytes));
        Assert.Equal(DatabaseErrorCode.InvalidParameters, ex.ErrorCode);
    }

    [Theory]
    [InlineData("[{\"$match\":{},\"$match\":{}}]")]
    [InlineData("[null]")]
    [InlineData("null")]
    public void Pipeline_CliAndWebRejectInvalidObjectArraysBeforeDictionaryBinding(string raw)
    {
        var tool = new ToolCatalog().Tools.Single(t => t.Name == "mongo_aggregate");
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindArguments(tool, new Dictionary<string, string?> { ["collection"] = "docs", ["pipeline"] = raw }));
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindJsonArguments(tool, new Dictionary<string, JsonElement>
        { ["collection"] = JsonSerializer.SerializeToElement("docs"), ["pipeline"] = JsonSerializer.Deserialize<JsonElement>(raw) }));
    }
}
