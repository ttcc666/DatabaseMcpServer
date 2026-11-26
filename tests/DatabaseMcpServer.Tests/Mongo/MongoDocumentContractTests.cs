using DatabaseMcpServer.Tools;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using MongoDB.Bson;

namespace DatabaseMcpServer.Tests;

public class MongoDocumentContractTests
{
    [Fact]
    public void SrvUri_IsRejectedByExecutionAndConfigurationWithoutExposingCredentials()
    {
        const string uri = "mongodb+srv://private-user:private-password@example.invalid/app";
        var error = Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateUri(uri));
        Assert.Equal(DatabaseErrorCode.InvalidParameters, error.ErrorCode);
        Assert.Contains("mongodb+srv", error.Message);
        Assert.DoesNotContain("private-", error.Message);
        Assert.Equal(error.Message, DatabaseProviderCatalog.ConnectionStringError(SqlSugar.DbType.MongoDb, uri));
        Assert.Null(DatabaseProviderCatalog.ConnectionStringError(SqlSugar.DbType.MongoDb, "mongodb://localhost/app"));
    }

    internal static Dictionary<string, JsonElement> Object(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    private static BsonDocument Parse(string json)
    {
        var bytes = 0;
        return MongoDocumentContract.Parse(Object(json), ref bytes, true);
    }

    [Fact]
    public void CanonicalExtendedJson_RoundTripsBsonTypes()
    {
        const string source = """
            {"_id":{"$oid":"507f1f77bcf86cd799439011"},"stringId":"507f1f77bcf86cd799439011",
             "long":{"$numberLong":"9007199254740993"},"decimal":{"$numberDecimal":"1234567890.123456789"},
             "date":{"$date":{"$numberLong":"1700000000123"}},"binary":{"$binary":{"base64":"AAECAw==","subType":"00"}},
             "uuid":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}},"nested":[null,{"ok":true}]}
            """;
        var value = Parse(source);
        Assert.Equal(BsonType.ObjectId, value["_id"].BsonType);
        Assert.Equal(BsonType.String, value["stringId"].BsonType);
        Assert.Equal(9007199254740993L, value["long"].AsInt64);
        Assert.Equal(Decimal128.Parse("1234567890.123456789"), value["decimal"].AsDecimal128);
        Assert.Equal(1700000000123, value["date"].AsBsonDateTime.MillisecondsSinceEpoch);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, value["binary"].AsBsonBinaryData.Bytes);
        Assert.Equal(BsonBinarySubType.UuidStandard, value["uuid"].AsBsonBinaryData.SubType);
        Assert.Equal(value, Parse(MongoDocumentContract.ToJson(value).GetRawText()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"object\"")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"nested\":[{\"a\":1,\"a\":2}]}")]
    public void RawObjectValidation_RejectsWrongShapesAndDuplicateKeys(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(DatabaseErrorCode.InvalidParameters, Assert.Throws<DatabaseMcpException>(
            () => MongoDocumentContract.ValidateObject(document.RootElement)).ErrorCode);
        var tool = new ToolCatalog().Tools.Single(t => t.Name == "mongo_find");
        Assert.ThrowsAny<Exception>(() => ToolInvoker.BindArguments(tool, new Dictionary<string, string?>
        { ["collection"] = "docs", ["filter"] = json }));
    }

    [Fact]
    public void InputLimits_RejectExcessiveDepthAndCumulativeSize()
    {
        using var deep = JsonDocument.Parse(string.Concat(Enumerable.Repeat("{\"n\":", 66)) + "0" + new string('}', 66), new() { MaxDepth = 128 });
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateObject(deep.RootElement));
        var size = 3 * 1024 * 1024;
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.Parse(Object(JsonSerializer.Serialize(new { payload = new string('x', 2 * 1024 * 1024) })), ref size));
    }

    [Theory]
    [InlineData("{\"x\":{\"$oid\":\"invalid\"}}")]
    [InlineData("{\"x\":{\"$numberLong\":\"999999999999999999999\"}}")]
    [InlineData("{\"x\":{\"$code\":\"secret()\"}}")]
    public void InvalidEjson_IsSafeInputError(string json)
    {
        var error = Assert.Throws<DatabaseMcpException>(() => Parse(json));
        Assert.Equal(DatabaseErrorCode.InvalidParameters, error.ErrorCode);
        Assert.DoesNotContain("secret()", error.Message);
    }

    [Theory]
    [InlineData("{\"$or\":[{\"nested\":{\"$where\":\"secret()\"}}]}")]
    [InlineData("{\"$expr\":{\"$function\":{\"body\":\"secret()\"}}}")]
    [InlineData("{\"x\":{\"$accumulator\":{}}}")]
    public void Filters_RejectJavascriptRecursively(string json)
        => Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateFilter(Parse(json)));

    [Theory]
    [InlineData("")]
    [InlineData("system.users")]
    [InlineData("bad$name")]
    [InlineData("nul\0name")]
    [InlineData("two words")]
    [InlineData("tab\tname")]
    [InlineData("line\nname")]
    public void CollectionRules_RejectReservedNames(string name)
        => Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateCollection(name));

    [Fact]
    public void ProjectionSortAndUpdate_EnforceExplicitContracts()
    {
        MongoDocumentContract.ValidateCollection("订单-archive.events");
        MongoDocumentContract.ValidateProjection(Parse("{\"x\":1,\"_id\":0}"));
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateProjection(Parse("{\"x\":1,\"y\":0}")));
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateProjection(Parse("{\"x\":true}")));
        var sort = Parse("{\"score\":-1}");
        MongoDocumentContract.ValidateSort(sort);
        Assert.Equal(new[] { "score", "_id" }, sort.Names);
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateSort(Parse("{\"x\":{\"$meta\":\"textScore\"}}")));
        MongoDocumentContract.ValidateUpdate(Parse("{\"$set\":{\"data\":{\"$set\":\"literal\"}}}"));
        foreach (var json in new[] { "{}", "{\"x\":1}", "{\"$rename\":{\"a\":\"b\"}}", "{\"$set\":{\"_id.x\":1}}", "{\"$inc\":[]}" })
            Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateUpdate(Parse(json)));
    }

    [Fact]
    public void BatchMutationsAndEmptyFilter_RequireExplicitDangerousOptIn()
    {
        foreach (var empty in new[] { false, true })
        foreach (var allowAll in new[] { false, true })
        foreach (var dangerous in new[] { false, true })
        {
            var filter = empty ? new BsonDocument() : new BsonDocument("x", 1);
            var error = Record.Exception(() => MongoDocumentContract.EnsureBatchMutationAllowed(filter, allowAll, dangerous));
            if (!dangerous || (empty && !allowAll))
                Assert.Equal(DatabaseErrorCode.AccessDenied, Assert.IsType<DatabaseMcpException>(error).ErrorCode);
            else Assert.Null(error);
        }
        Assert.Equal("App", MongoDocumentContract.ValidateUri("mongodb://User:Secret@localhost:27017/App?authSource=admin").DatabaseName);
        Assert.Throws<DatabaseMcpException>(() => MongoDocumentContract.ValidateUri("mongodb://localhost/?authSource=admin"));
    }
}
