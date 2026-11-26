using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class TableQueryOptionsTests
{
    [Theory]
    [InlineData(TableFilterOperator.Eq, "2")]
    [InlineData(TableFilterOperator.Ne, "1,3")]
    [InlineData(TableFilterOperator.Gt, "3")]
    [InlineData(TableFilterOperator.Gte, "2,3")]
    [InlineData(TableFilterOperator.Lt, "1")]
    [InlineData(TableFilterOperator.Lte, "1,2")]
    public async Task Comparisons_RespectTypedBoundaries(TableFilterOperator op, string expected)
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY); INSERT INTO items VALUES(1),(2),(3)");
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<QueryTools>().QueryTablePage("items",
            filters: [new() { Column = "id", Op = op, Value = JsonSerializer.SerializeToElement("2"), DbType = DatabaseParameterType.Int64 }]));
        Assert.Equal(expected, string.Join(',', result.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32())));
    }

    [Fact]
    public async Task FilterValues_AreParameterizedAndTyped()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT); INSERT INTO items VALUES(1,null),(2,'null'),(3,'O''Brien'),(4,'null')");
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        var filtered = DatabaseRoleTests.Success(await tool.QueryTablePage("items", filters:
        [
            new() { Column = "name", Op = TableFilterOperator.Eq, Value = JsonSerializer.SerializeToElement("null") },
            new() { Column = "id", Op = TableFilterOperator.Gt, Value = JsonSerializer.SerializeToElement(2) }
        ]));
        Assert.Equal(4, Assert.Single(filtered.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt32());
        foreach (var (value, count) in new[] { ("O'Brien", 1), ("' OR 1=1 --", 0) })
        {
            var result = DatabaseRoleTests.Success(await tool.QueryTablePage("items", filters:
                [new() { Column = "name", Op = TableFilterOperator.Eq, Value = JsonSerializer.SerializeToElement(value) }]));
            Assert.Equal(count, result.GetProperty("rowCount").GetInt32());
        }
        var nulls = DatabaseRoleTests.Success(await tool.QueryTablePage("items", filters: [new() { Column = "name", Op = TableFilterOperator.IsNull }]));
        Assert.Equal(1, Assert.Single(nulls.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt32());
        var nonNulls = DatabaseRoleTests.Success(await tool.QueryTablePage("items", filters: [new() { Column = "name", Op = TableFilterOperator.IsNotNull }]));
        Assert.Equal(new[] { 2, 3, 4 }, nonNulls.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
    }

    [Theory]
    [InlineData(TableSortDirection.Asc, "c,d,b,a")]
    [InlineData(TableSortDirection.Desc, "a,b,d,c")]
    public async Task ProjectionAndOrdering_PageWithoutDuplicates(TableSortDirection direction, string expected)
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(a INT NOT NULL,b INT NOT NULL,label TEXT,rank INT,PRIMARY KEY(b,a)); INSERT INTO items VALUES(2,1,'a',7),(1,1,'b',7),(1,2,'c',null),(2,2,'d',null)");
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        var labels = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = DatabaseRoleTests.Success(await tool.QueryTablePage("items", page: page, pageSize: 2, columns: ["label"],
                orderBy: [new() { Column = "rank", Direction = direction }, new() { Column = "a", Direction = direction }]));
            Assert.Equal("offset", result.GetProperty("pagingMode").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("nextCursor").ValueKind);
            Assert.Equal(new[] { "rank", "a", "b" }, result.GetProperty("orderBy").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal("asc", result.GetProperty("effectiveOrderBy")[2].GetProperty("direction").GetString());
            Assert.Equal(page < 3, result.GetProperty("mayHaveMore").GetBoolean());
            foreach (var row in result.GetProperty("data").EnumerateArray())
            {
                Assert.Equal("label", Assert.Single(row.EnumerateObject()).Name);
                labels.Add(row.GetProperty("label").GetString()!);
            }
        }
        Assert.Equal(expected, string.Join(',', labels));
    }

    [Fact]
    public async Task Options_PreserveColumnNamesAndScalarValues()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY, DisplayName TEXT, amount DECIMAL(18,2), ratio REAL, payload BLOB); INSERT INTO items VALUES(9007199254740993,'中文',10.25,1.5,X'0001FF'),(9007199254740994,NULL,NULL,NULL,NULL)");
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<QueryTools>().QueryTablePage("items", filters: []));
        var rows = result.GetProperty("data");
        Assert.Equal(2, result.GetProperty("rowCount").GetInt32());
        Assert.Equal(new[] { "id", "DisplayName", "amount", "ratio", "payload" }, rows[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(9007199254740993L, rows[0].GetProperty("id").GetInt64());
        Assert.Equal("中文", rows[0].GetProperty("DisplayName").GetString());
        Assert.Equal(10.25m, rows[0].GetProperty("amount").GetDecimal());
        Assert.Equal(1.5, rows[0].GetProperty("ratio").GetDouble());
        Assert.Equal(new byte[] { 0, 1, 255 }, rows[0].GetProperty("payload").GetBytesFromBase64());
        Assert.Equal(9007199254740994L, rows[1].GetProperty("id").GetInt64());
        Assert.All(rows[1].EnumerateObject().Where(p => p.Name != "id"), p => Assert.Equal(JsonValueKind.Null, p.Value.ValueKind));
    }

    [Fact]
    public async Task InvalidOptions_AreRejected()
    {
        using var fixture = DatabaseRoleTests.Create();
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        foreach (var columns in new[] { Array.Empty<string>(), new[] { "missing" }, new[] { "value", "value" }, new[] { "value;DELETE FROM markers" }, new[] { "markers.value" }, new string[65] })
            DatabaseRoleTests.Error(await tool.QueryTablePage("markers", columns: columns), 1004);
        foreach (var filter in new TableFilter[]
        {
            new() { Column = "value", Op = (TableFilterOperator)100 },
            new() { Column = "missing", Op = TableFilterOperator.IsNull },
            new() { Column = "value", Op = TableFilterOperator.Eq },
            new() { Column = "value", Op = TableFilterOperator.Eq, Value = JsonSerializer.SerializeToElement(new[] { 1 }) },
            new() { Column = "value", Op = TableFilterOperator.Eq, Value = JsonSerializer.SerializeToElement(new { a = 1 }) },
            new() { Column = "value", Op = TableFilterOperator.IsNull, Value = JsonSerializer.SerializeToElement(1) },
            new() { Column = "value", Op = TableFilterOperator.IsNotNull, DbType = DatabaseParameterType.Int32 },
            null!
        }) DatabaseRoleTests.Error(await tool.QueryTablePage("markers", filters: [filter]), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("markers", filters: new TableFilter[33]), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("markers", orderBy: new TableOrder[9]), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("markers", orderBy: [new() { Column = "value", Direction = (TableSortDirection)9 }]), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("markers", orderBy: [new() { Column = "value" }, new() { Column = "value" }]), 1004);
        var first = DatabaseRoleTests.Success(await tool.QueryTablePage("markers", pageSize: 1));
        DatabaseRoleTests.Error(await tool.QueryTablePage("markers", pageSize: 1, cursor: first.GetProperty("nextCursor").GetString(), filters: []), 1004);
        var emptyOptions = DatabaseRoleTests.Success(await tool.QueryTablePage("markers", filters: [], orderBy: []));
        Assert.Equal("offset", emptyOptions.GetProperty("pagingMode").GetString());
        Assert.Equal("value", Assert.Single(emptyOptions.GetProperty("data")[0].EnumerateObject()).Name);
        Assert.Equal(1, emptyOptions.GetProperty("data")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Options_RespectBudgetAndReleaseLease()
    {
        using var fixture = DatabaseRoleTests.Create(new DatabaseExecutionOptions(MaxConcurrent: 1) { MaxQueryRows = 10 });
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO items VALUES(1,hex(zeroblob(10000)))");
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        DatabaseRoleTests.Error(await tool.QueryTablePage("items", pageSize: 1, columns: ["value"], maxResponseBytes: 2048), 1013);
        DatabaseRoleTests.Error(await tool.QueryTablePage("items", pageSize: 11, columns: ["id"]), 1004);
        using (var held = fixture.Sessions.AcquireClient())
            DatabaseRoleTests.Error(await tool.QueryTablePage("items", pageSize: 1, columns: ["id"], commandTimeoutSeconds: 1), 1011);
        var result = DatabaseRoleTests.Success(await tool.QueryTablePage("items", pageSize: 1, columns: ["id"]));
        Assert.Equal(1, Assert.Single(result.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt32());
    }
}
