using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ReaderPerformanceTests
{
    [Fact]
    public async Task CompleteResult_RawRowsAvoidDictionaryRoundTrip()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        lease.Client.Ado.ExecuteCommand("CREATE TABLE complete_rows(id integer PRIMARY KEY,value text); WITH RECURSIVE n(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM n WHERE id<10000) INSERT INTO complete_rows SELECT id,hex(zeroblob(64)) FROM n");
        async Task<byte[]> Run(bool roundTrip)
        {
            var result = await new BoundedQueryExecutor(10001, 4 * 1024 * 1024, 1024)
                .QueryResultAsync(lease.Context, "SELECT * FROM complete_rows ORDER BY id", [], default);
            Assert.Equal(10000, result.RowCount);
            Assert.False(result.LimitReached);
            Assert.False(result.Truncated);
            if (!roundTrip) return JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase);
            var previous = new DatabaseQueryResult
            {
                Success = result.Success, DatabaseName = result.DatabaseName, DatabaseType = result.DatabaseType,
                ConfigurationVersion = result.ConfigurationVersion, RowCount = result.RowCount,
                Data = result.Data.Select(r => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.Utf8Json)!).ToList(),
                LimitReached = result.LimitReached, Truncated = result.Truncated
            };
            return JsonSerializer.SerializeToUtf8Bytes(previous, JsonSerializationDefaults.CompactCamelCase);
        }
        var expected = await Run(true);
        Assert.Equal(expected, await Run(false));
    }

    [Fact]
    public async Task ReaderBudget_AvoidsMaterializingOversizedPage()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        var db = lease.Client;
        db.Ado.ExecuteCommand("CREATE TABLE perf_rows(id integer PRIMARY KEY,value text); WITH RECURSIVE n(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM n WHERE id<10000) INSERT INTO perf_rows SELECT id,hex(zeroblob(512)) FROM n");
        using var table = await db.SqlQueryable<object>("SELECT * FROM perf_rows").OrderBy("id").ToDataTablePageAsync(1, 10000);
        var data = fixture.Host.Services.GetRequiredService<IDatabaseHelperService>().ConvertDataTableToList(table);
        Assert.Equal(10000, data.Count);
        var failure = Assert.Throws<DatabaseMcpException>(() => BoundedQueryExecutor.EncodeResponse(new { success = true, data }, 4096));
        Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, failure.ErrorCode);

        var result = await new BoundedQueryExecutor(10000, 4096, 1024).QueryResultAsync(lease.Context,
            "SELECT * FROM perf_rows ORDER BY id LIMIT 10000", [], default);
        Assert.True(result.Truncated);
        Assert.InRange(result.RowCount, 1, 4);
        using var first = JsonDocument.Parse(result.Data[0].Utf8Json);
        Assert.Equal(1, first.RootElement.GetProperty("id").GetInt32());
    }
}
