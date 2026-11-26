using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class ExplainToolsTests
{
    [Fact]
    public async Task EstimatedPlan_DoesNotEvaluatePoisonedExpressionAndBindsParameters()
    {
        using var fixture = DatabaseRoleTests.Create();
        var tool = fixture.Host.Services.GetRequiredService<ExplainTools>();
        var result = DatabaseRoleTests.Success(await tool.ExplainQuery("select abs(-9223372036854775808) from markers where value=@v",
            parameters: [new() { Name = "v", Value = JsonSerializer.SerializeToElement(1) }]).Json());
        Assert.True(result.GetProperty("estimated").GetBoolean());
        Assert.False(result.GetProperty("executed").GetBoolean());
        Assert.Contains("SEARCH", result.GetProperty("plan").ToString());
        var query = await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select abs(-9223372036854775808) from markers");
        Assert.True(query.IsError);
        DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select * from markers").Json());
    }

    [Theory]
    [InlineData("EXPLAIN ANALYZE SELECT 1")]
    [InlineData("SET SHOWPLAN_XML ON")]
    [InlineData("select 1; select 2")]
    [InlineData("delete from markers")]
    public async Task EstimatedPlan_RejectsNonQueryInputs(string sql)
    {
        using var fixture = DatabaseRoleTests.Create();
        Assert.True((await fixture.Host.Services.GetRequiredService<ExplainTools>().ExplainQuery(sql)).IsError);
        var rows = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select * from markers").Json());
        Assert.Equal(1, rows.GetProperty("rowCount").GetInt32());
    }
}
