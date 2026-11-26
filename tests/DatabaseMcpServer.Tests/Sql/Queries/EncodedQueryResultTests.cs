using System.Data;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class EncodedQueryResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawRows_PreserveCompletePublicContractAndValueTypes(bool empty)
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        using var table = new DataTable();
        table.Columns.Add("BigId", typeof(long));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("label", typeof(string));
        table.Columns.Add("bytes", typeof(byte[]));
        table.Columns.Add("missing", typeof(string));
        if (!empty) table.Rows.Add(9007199254740993L, 123456789.123456789m, "汉字 😀 \" \\ \n <>&", new byte[] { 0, 128, 255 }, DBNull.Value);
        using var reader = table.CreateDataReader();
        var result = await new BoundedQueryExecutor(10, 32768, 1024).ReadResultAsync(lease.Context, reader, default);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase);
        var contract = JsonSerializer.Deserialize<DatabaseQueryResult>(encoded, JsonSerializationDefaults.CompactCamelCase)!;
        Assert.True(contract.Success);
        Assert.Equal(lease.Context.DatabaseName, contract.DatabaseName);
        Assert.Equal(lease.Context.ConfigurationVersion, contract.ConfigurationVersion);
        Assert.Equal(empty ? 0 : 1, contract.RowCount);
        Assert.False(contract.LimitReached);
        Assert.False(contract.Truncated);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(contract, JsonSerializationDefaults.CompactCamelCase), encoded);
        if (empty) Assert.Empty(contract.Data);
        else
        {
            var row = Assert.Single(contract.Data);
            Assert.Equal(9007199254740993L, row["BigId"].GetInt64());
            Assert.Equal(123456789.123456789m, row["amount"].GetDecimal());
            Assert.Equal("汉字 😀 \" \\ \n <>&", row["label"].GetString());
            Assert.Equal(new byte[] { 0, 128, 255 }, row["bytes"].GetBytesFromBase64());
            Assert.Equal(JsonValueKind.Null, row["missing"].ValueKind);
        }
    }

    [Fact]
    public async Task RawRows_KeepOnlyWholeRowsAtByteBoundary()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        const string sql = "SELECT 1 AS id, '汉字' AS label UNION ALL SELECT 2, '汉字'";
        var complete = await new BoundedQueryExecutor(10, 8192, 1024).QueryResultAsync(lease.Context, sql, [], default);
        var firstCost = complete.Data[0].Utf8Json.Length + 1;
        foreach (var (available, count) in new[] { (firstCost - 1, 0), (firstCost, 1), (firstCost + 1, 1) })
        {
            var result = await new BoundedQueryExecutor(10, 1024 + available, 1024).QueryResultAsync(lease.Context, sql, [], default);
            Assert.Equal(count, result.RowCount);
            Assert.True(result.LimitReached);
            Assert.True(result.Truncated);
            var payload = BoundedQueryExecutor.EncodeResponse(result, 1024 + available);
            using var json = JsonDocument.Parse(payload.Utf8Json);
            Assert.Equal(count, json.RootElement.GetProperty("data").GetArrayLength());
            if (count == 1) Assert.Equal(1, json.RootElement.GetProperty("data")[0].GetProperty("id").GetInt32());
        }
    }
}
