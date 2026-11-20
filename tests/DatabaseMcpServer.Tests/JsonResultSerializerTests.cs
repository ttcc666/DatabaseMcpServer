using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tests;

public class JsonResultSerializerTests
{
    [Fact]
    public void Serialize_PreencodedResultPreservesJsonAndUtf8ByteBudget()
    {
        const string expected = """{"success":true,"data":[{"名":"中文😀","escaped":"line\n\"quoted\"\\","value":1.2300,"missing":null}],"limitReached":true,"truncated":null}""";
        var bytes = Encoding.UTF8.GetBytes(expected);

        var result = new JsonResultSerializer().Serialize(new BoundedJsonResult(bytes));

        Assert.Equal(expected, result);
        Assert.Equal(bytes.Length, Encoding.UTF8.GetByteCount(result));
        using var json = JsonDocument.Parse(result);
        var row = json.RootElement.GetProperty("data")[0];
        Assert.Equal("中文😀", row.GetProperty("名").GetString());
        Assert.Equal("line\n\"quoted\"\\", row.GetProperty("escaped").GetString());
        Assert.Equal(1.23m, row.GetProperty("value").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("missing").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("truncated").ValueKind);
    }

    [Fact]
    public void Serialize_OrdinaryResultsKeepIndentedCamelCaseAndStringEscaping()
    {
        var result = new JsonResultSerializer().Serialize(new { Success = true, Message = "中文\n\"文本\"", Missing = (object?)null });

        var expected = string.Join(Environment.NewLine,
            "{", "  \"success\": true,", "  \"message\": \"中文\\n\\\"文本\\\"\",", "  \"missing\": null", "}");
        Assert.Equal(expected, result);
        Assert.Equal("\"{\\\"success\\\":true}\"", new JsonResultSerializer().Serialize("{\"success\":true}"));
        Assert.Equal("null", new JsonResultSerializer().Serialize(null!));
    }

    [Fact]
    public void Serialize_NestedPreencodedResultsRemainJsonObjects()
    {
        var result = new JsonResultSerializer().Serialize(new
        {
            Success = true,
            Results = new[]
            {
                new BoundedJsonResult("{\"value\":7,\"label\":\"中文\"}"u8.ToArray()),
                new BoundedJsonResult("{\"data\":[]}"u8.ToArray())
            }
        });

        using var json = JsonDocument.Parse(result);
        var results = json.RootElement.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal(JsonValueKind.Object, results[0].ValueKind);
        Assert.Equal(7, results[0].GetProperty("value").GetInt32());
        Assert.Equal("中文", results[0].GetProperty("label").GetString());
        Assert.Empty(results[1].GetProperty("data").EnumerateArray());
        Assert.DoesNotContain("utf8Json", result);
    }
}
