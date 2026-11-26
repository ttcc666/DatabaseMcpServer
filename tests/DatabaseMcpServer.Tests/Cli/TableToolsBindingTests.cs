using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class TableToolsBindingTests
{
    [Fact]
    public async Task CliAndWeb_BindTableOptionsAndRequireInsertConfirmation()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE binding_items(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT)");
        var runner = new CliRunner();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var args = new[] { "tool", "insert_row", "--config", fixture.Path, "--database-name", "primary", "--table-name", "binding_items", "--row", "{\"name\":\"cli\"}" };
        Assert.Equal(2, await runner.RunAsync(args, output, error));
        Assert.Contains("--yes", error.ToString());
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Assert.Equal(0, await runner.RunAsync([..args, "--yes"], output, error));
        Assert.Equal("1", DatabaseRoleTests.Success(output.ToString()).GetProperty("identity").GetString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await runner.RunAsync(["tool", "query_table_page", "--config", fixture.Path, "--database-name", "primary",
            "--table-name", "binding_items", "--columns", "[\"name\"]", "--filters", "[{\"column\":\"name\",\"op\":\"eq\",\"value\":\"cli\"}]",
            "--order-by", "[{\"column\":\"id\",\"direction\":\"desc\"}]"], output, error));
        Assert.Equal("cli", DatabaseRoleTests.Success(output.ToString()).GetProperty("data")[0].GetProperty("name").GetString());

        var web = fixture.Host.Services.GetRequiredService<CliWebToolService>();
        var request = new Dictionary<string, JsonElement>
        {
            ["table-name"] = JsonSerializer.SerializeToElement("binding_items"),
            ["row"] = JsonSerializer.SerializeToElement(new { name = "web" })
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync("insert_row", new(request, null), default));
        var inserted = JsonSerializer.SerializeToElement(await web.InvokeAsync("insert_row", new(request, "insert_row"), default));
        Assert.True(inserted.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal("2", inserted.GetProperty("result").GetProperty("identity").GetString());
        var queryArgs = new Dictionary<string, JsonElement>
        {
            ["table-name"] = JsonSerializer.SerializeToElement("binding_items"),
            ["columns"] = JsonSerializer.SerializeToElement(new[] { "name" }),
            ["filters"] = JsonSerializer.SerializeToElement(new[] { new { column = "name", op = "eq", value = "web" } }),
            ["order-by"] = JsonSerializer.SerializeToElement(new[] { new { column = "id", direction = "desc" } })
        };
        var selected = JsonSerializer.SerializeToElement(await web.InvokeAsync("query_table_page", new(queryArgs, null), default));
        Assert.True(selected.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal("web", selected.GetProperty("result").GetProperty("data")[0].GetProperty("name").GetString());
        queryArgs["filters"] = JsonSerializer.Deserialize<JsonElement>("[{\"column\":\"id\",\"op\":\"drop\",\"value\":1}]");
        await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync("query_table_page", new(queryArgs, null), default));
        using var observer = fixture.Sessions.AcquireClient();
        Assert.Equal(2, observer.Client.Ado.GetInt("SELECT count(*) FROM binding_items"));
    }
}
