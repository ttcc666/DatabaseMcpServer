using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class BatchQueryCommandTests
{
    [Fact]
    public async Task Commands_BindsEachItemsParametersAndSharesRowBudget()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        var commands = new DatabaseCommand[]
        {
            new() { Sql = "select @value as value", Parameters = [new() { Name = "value", Value = JsonSerializer.SerializeToElement(17) }] },
            new() { Sql = "select @value as value", Parameters = [new() { Name = "value", Value = JsonSerializer.SerializeToElement(29) }] },
            new() { Sql = "select 99 as value" }
        };
        using var response = JsonDocument.Parse(await tool.BatchSqlQuery(commands: commands, maxRows: 2));
        var results = response.RootElement.GetProperty("results");
        Assert.Equal(17, results[0].GetProperty("data")[0].GetProperty("value").GetInt32());
        Assert.Equal(29, results[1].GetProperty("data")[0].GetProperty("value").GetInt32());
        Assert.False(results[2].GetProperty("executed").GetBoolean());
    }

    [Fact]
    public async Task Commands_RejectsAmbiguousOrMissingInputAndKeepsPerItemFailures()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        foreach (var payload in new[] { await tool.BatchSqlQuery(null!), await tool.BatchSqlQuery([]) })
        {
            using var invalid = JsonDocument.Parse(payload);
            Assert.Equal(1004, invalid.RootElement.GetProperty("errorCode").GetInt32());
        }
        using var response = JsonDocument.Parse(await tool.BatchSqlQuery(commands:
            [new() { Sql = "delete from anything" }, new() { Sql = "select 7 as value" }]));
        Assert.False(response.RootElement.GetProperty("results")[0].GetProperty("executed").GetBoolean());
        Assert.Equal(7, response.RootElement.GetProperty("results")[1].GetProperty("data")[0].GetProperty("value").GetInt32());
    }
}
