using System.Text.Json;
using DatabaseMcpServer.Models;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class DatabaseRoleIntegrationTests
{
    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task TypedQueryAndCommand_PreserveNullNumericDateAndArrayValues(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "dbmcp_" + Guid.NewGuid().ToString("N");
        var dateType = provider == "SqlServer" ? "datetime2" : provider == "MySql" ? "datetime" : "timestamp";
        var stringType = provider == "SqlServer" ? "nvarchar(64)" : "varchar(64)";
        fixture.Observer.Ado.ExecuteCommand($"create table {table} (id bigint primary key, amount decimal(18,4), optional_value bigint null, happened {dateType}, label {stringType})");
        try
        {
            using var inserted = ProviderFixture.Success(await fixture.Command.ExecuteCommand(
                $"insert into {table} values (@id,@amount,@optional,@happened,@label)", parameters:
                [
                    P("id", DatabaseParameterType.Int64, "9007199254740993"),
                    new() { Name = "amount", DbType = DatabaseParameterType.Decimal, Value = JsonSerializer.SerializeToElement(12345678901234.5678m), Precision = 18, Scale = 4 },
                    P("optional", DatabaseParameterType.Int64, null),
                    P("happened", DatabaseParameterType.DateTime, "2026-11-20T12:34:56"),
                    P("label", DatabaseParameterType.String, "中文 ' quote")
                ]));
            Assert.Equal(1, inserted.RootElement.GetProperty("affectedRows").GetInt32());
            var result = await fixture.StructuredQuery.QueryDatabase($"select * from {table} where id in (@ids)", "integration",
                [new() { Name = "ids", Value = JsonSerializer.SerializeToElement(new[] { 9007199254740993L, 2L }) }]);
            using var payload = ProviderFixture.Success(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
            Assert.False(result.IsError);
            Assert.True(JsonElement.DeepEquals(payload.RootElement, result.StructuredContent!.Value));
            var row = Assert.Single(payload.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(9007199254740993L, row.GetProperty("id").GetInt64());
            Assert.Equal(12345678901234.5678m, row.GetProperty("amount").GetDecimal());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("optional_value").ValueKind);
            Assert.Equal(new DateTime(2026, 11, 20, 12, 34, 56), row.GetProperty("happened").GetDateTime());
            Assert.Equal("中文 ' quote", row.GetProperty("label").GetString());
            using var page = ProviderFixture.Success(await fixture.Query.QueryTablePage(table, pageSize: 1));
            Assert.Equal(9007199254740993L, page.RootElement.GetProperty("data")[0].GetProperty("id").GetInt64());
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"drop table {table}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task BoundedMultiple_MatchesNativeDataSetMappingsAndStopsAtBudget(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var binary = provider switch { "PostgreSQL" => "decode('00ff','hex')", "MySql" => "X'00ff'", _ => "0x00ff" };
        var date = provider == "SqlServer" ? "datetime2" : provider == "MySql" ? "datetime" : "timestamp";
        var sql = $"select cast(9007199254740993 as bigint) as id, cast(1234.5678 as decimal(18,4)) as amount, cast('2026-11-20 12:34:56' as {date}) as happened, {binary} as bytes, NULL as missing, 1 as a, 2 as a; select 1 as empty_value where 1=0; select 3 as v";
        // MySQL uses SIGNED instead of BIGINT in CAST.
        if (provider == "MySql") sql = sql.Replace("as bigint", "as signed");
        using var native = fixture.Observer.Ado.GetDataSetAll(sql);
        var expected = JsonSerializer.SerializeToElement(native.Tables.Cast<System.Data.DataTable>().Select(table => new
        {
            rowCount = table.Rows.Count,
            data = table.Rows.Cast<System.Data.DataRow>().Select(row => table.Columns.Cast<System.Data.DataColumn>()
                .ToDictionary(column => column.ColumnName, column => row[column] == DBNull.Value ? null : row[column])).ToArray()
        }).ToArray());
        using var bounded = ProviderFixture.Success(await fixture.Query.GetDataSetAll(sql, maxRows: 10, maxResponseBytes: 8192));
        Assert.Equal(3, bounded.RootElement.GetProperty("resultSetCount").GetInt32());
        Assert.True(JsonElement.DeepEquals(expected, bounded.RootElement.GetProperty("resultSets")), bounded.RootElement.GetRawText());
        using var stopped = ProviderFixture.Success(await fixture.Query.GetDataSetAll("select 1 as v; select 2 as v", maxRows: 1));
        Assert.Equal(1, stopped.RootElement.GetProperty("rowCount").GetInt32());
        Assert.True(stopped.RootElement.GetProperty("limitReached").GetBoolean());
        Assert.Equal(JsonValueKind.Null, stopped.RootElement.GetProperty("truncated").ValueKind);
        await fixture.AssertUsable();
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task TypedProcedure_PreservesOutputAndInputOutputTypes(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var procedure = "dbmcp_" + Guid.NewGuid().ToString("N");
        var sql = provider == "MySql"
            ? $"CREATE PROCEDURE {procedure}(INOUT counter_value BIGINT, OUT label_value VARCHAR(64)) BEGIN SET counter_value=counter_value+1; SET label_value='done'; SELECT counter_value AS received; END"
            : $"CREATE PROCEDURE {procedure} @counter_value BIGINT OUTPUT, @label_value VARCHAR(64) OUTPUT AS BEGIN SET NOCOUNT ON; SET @counter_value=@counter_value+1; SET @label_value='done'; SELECT @counter_value AS received; END";
        fixture.Observer.Ado.ExecuteCommand(sql);
        try
        {
            using var result = ProviderFixture.Success(await fixture.Command.CallStoredProcedure(procedure, parameters:
            [
                new() { Name = "@counter_value", DbType = DatabaseParameterType.Int64, Direction = DatabaseParameterDirection.InputOutput, Value = JsonSerializer.SerializeToElement(9007199254740993L) },
                new() { Name = "@label_value", DbType = DatabaseParameterType.AnsiString, Direction = DatabaseParameterDirection.Output, Size = 64 }
            ]));
            var outputs = result.RootElement.GetProperty("outputParameters").EnumerateObject().ToDictionary(p => p.Name.TrimStart('@'), p => p.Value);
            Assert.Equal(9007199254740994L, outputs["counter_value"].GetInt64());
            Assert.Equal("done", outputs["label_value"].GetString());
            Assert.Equal(9007199254740994L, result.RootElement.GetProperty("data")[0].GetProperty("received").GetInt64());
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP PROCEDURE {procedure}"); }
    }

    private static DatabaseParameter P(string name, DatabaseParameterType type, object? value)
        => new() { Name = name, DbType = type, Value = JsonSerializer.SerializeToElement(value) };

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task TypedOffset_PreservesInstantAcrossProviderTimezoneRules(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var result = await fixture.StructuredQuery.QueryDatabase("select @instant as instant", parameters:
            [P("instant", DatabaseParameterType.DateTimeOffset, "2026-11-20T12:34:56+08:00")]);
        using var payload = ProviderFixture.Success(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 4, 34, 56, TimeSpan.Zero),
            payload.RootElement.GetProperty("data")[0].GetProperty("instant").GetDateTimeOffset().ToUniversalTime());
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task StructuredQueryCancellation_StopsServerActivityAndReleasesLease(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var marker = "dbmcp_" + Guid.NewGuid().ToString("N");
        using var cancel = new CancellationTokenSource();
        var request = fixture.StructuredQuery.QueryDatabase(fixture.SlowQuery(marker), cancellationToken: cancel.Token);
        await fixture.WaitForActivity(marker, true);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await fixture.WaitForActivity(marker, false);
        await fixture.AssertUsable();
    }
}
