using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DatabaseMcpServer.Web;

namespace DatabaseMcpServer.Tests;

public class CliWebHostTests
{
    [Fact]
    public async Task StartAsync_ShouldServeWebAppAndManageConfigLifecycle()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-web-config-{Guid.NewGuid():N}.json");
        var stateFilePath = Path.Combine(Path.GetTempPath(), $"dbmcp-web-state-{Guid.NewGuid():N}.json");
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"dbmcp-web-sqlite-{Guid.NewGuid():N}.db");
        var host = new CliWebHost(new TestBrowserLauncher(), stateFilePath);

        DeleteFileIfExists(configPath);
        DeleteFileIfExists(stateFilePath);
        DeleteFileIfExists(sqlitePath);

        try
        {
            await using var handle = await host.StartAsync(
                new CliWebCommandOptions(configPath, null, false));

            using var client = new HttpClient
            {
                BaseAddress = handle.BaseAddress
            };

            var context = await client.GetFromJsonAsync<JsonElement>("/api/context");
            Assert.True(context.GetProperty("success").GetBoolean());
            Assert.Equal(Path.GetFullPath(configPath), context.GetProperty("configPath").GetString());
            Assert.False(context.GetProperty("configExists").GetBoolean());

            var rootResponse = await client.GetStringAsync("/");
            Assert.Contains("DatabaseMcpServer", rootResponse, StringComparison.Ordinal);
            Assert.True(
                rootResponse.Contains("Local Ops Console", StringComparison.Ordinal)
                || rootResponse.Contains("本地运维控制台", StringComparison.Ordinal)
                || rootResponse.Contains("Web Config", StringComparison.Ordinal),
                "Root HTML should identify the local web console.");

            var initResponse = await PostJsonAsync(client, "/api/config/init", new { force = false });
            Assert.True(initResponse.GetProperty("success").GetBoolean());
            Assert.True(File.Exists(configPath));

            var addResponse = await PostJsonAsync(client, "/api/databases", new
            {
                name = "sqlite-local",
                dbType = "Sqlite",
                connectionString = $"Data Source={sqlitePath};Cache=Shared;Mode=ReadWriteCreate;",
                description = "local sqlite",
                setDefault = true
            });
            Assert.True(addResponse.GetProperty("success").GetBoolean());

            var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
            Assert.True(dashboard.GetProperty("success").GetBoolean());
            Assert.Equal(1, dashboard.GetProperty("totalDatabases").GetInt32());
            Assert.Equal("sqlite-local", dashboard.GetProperty("currentDefaultDatabase").GetString());
            Assert.Equal("sqlite-local", dashboard.GetProperty("currentDatabase").GetString());
            Assert.Contains("Data Source=", dashboard.GetProperty("databases")[0].GetProperty("connectionString").GetString(), StringComparison.Ordinal);

            var testResponse = await PostJsonAsync(client, "/api/databases/sqlite-local/test", new { });
            Assert.True(testResponse.GetProperty("success").GetBoolean());
            Assert.True(testResponse.GetProperty("connected").GetBoolean());

            var profile = await client.GetFromJsonAsync<JsonElement>("/api/connection-string-profiles/Sqlite");
            Assert.True(profile.GetProperty("success").GetBoolean());
            Assert.True(profile.GetProperty("profile").GetProperty("supportsWizard").GetBoolean());

            var health = await PostJsonAsync(client, "/api/databases/health-check", new { });
            Assert.True(health.GetProperty("success").GetBoolean());
            Assert.Equal(1, health.GetProperty("totalConnections").GetInt32());
            Assert.True(health.GetProperty("results")[0].GetProperty("isHealthy").GetBoolean());

            var tools = await client.GetFromJsonAsync<JsonElement>("/api/tools");
            Assert.True(tools.GetProperty("success").GetBoolean());
            var listDatabases = Assert.Single(
                tools.GetProperty("tools").EnumerateArray(),
                tool => tool.GetProperty("name").GetString() == "list_databases");
            Assert.False(listDatabases.TryGetProperty("toolType", out _));
            Assert.False(listDatabases.TryGetProperty("method", out _));

            var safeInvocation = await PostToolJsonAsync(client, "/api/tools/list_databases/invoke", new
            {
                arguments = new { },
                confirmation = (string?)null
            });
            Assert.Equal(HttpStatusCode.OK, safeInvocation.StatusCode);
            Assert.True(safeInvocation.Payload.GetProperty("success").GetBoolean());
            Assert.True(safeInvocation.Payload.GetProperty("toolSuccess").GetBoolean());

            var unknownArgument = await PostToolJsonAsync(client, "/api/tools/list_databases/invoke", new
            {
                arguments = new Dictionary<string, object> { ["unexpected"] = true }
            });
            Assert.Equal(HttpStatusCode.BadRequest, unknownArgument.StatusCode);

            var unconfirmed = await PostToolJsonAsync(client, "/api/tools/drop_table/invoke", new
            {
                arguments = new Dictionary<string, object> { ["table-name"] = "web_tool_temp" }
            });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
            Assert.Contains("完整名称", unconfirmed.Payload.GetProperty("message").GetString(), StringComparison.Ordinal);

            var temporaryTableName = $"web_tool_{Guid.NewGuid():N}";
            var createInvocation = await PostToolJsonAsync(client, "/api/tools/create_table/invoke", new
            {
                arguments = new Dictionary<string, object>
                {
                    ["table-name"] = temporaryTableName,
                    ["columns-info"] = "[{\"DbColumnName\":\"id\",\"DataType\":\"integer\",\"IsPrimarykey\":true}]"
                },
                confirmation = "create_table"
            });
            Assert.Equal(HttpStatusCode.OK, createInvocation.StatusCode);
            Assert.True(createInvocation.Payload.GetProperty("toolSuccess").GetBoolean());

            var dropInvocation = await PostToolJsonAsync(client, "/api/tools/drop_table/invoke", new
            {
                arguments = new Dictionary<string, object> { ["table-name"] = temporaryTableName },
                confirmation = "drop_table"
            });
            Assert.Equal(HttpStatusCode.OK, dropInvocation.StatusCode);
            Assert.True(dropInvocation.Payload.GetProperty("toolSuccess").GetBoolean());

            var missingMarker = await PostToolJsonAsync(client, "/api/tools/list_databases/invoke", new { arguments = new { } }, includeMarker: false);
            Assert.Equal(HttpStatusCode.Forbidden, missingMarker.StatusCode);

            var crossOrigin = await PostToolJsonAsync(client, "/api/tools/list_databases/invoke", new { arguments = new { } }, origin: "http://example.test");
            Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);

            var sameOrigin = await PostToolJsonAsync(client, "/api/tools/list_databases/invoke", new { arguments = new { } }, origin: handle.BaseAddress.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.OK, sameOrigin.StatusCode);

            var switchResponse = await PostJsonAsync(client, "/api/current-database/switch", new
            {
                databaseName = "sqlite-local"
            });
            Assert.True(switchResponse.GetProperty("success").GetBoolean());
            Assert.Equal("sqlite-local", switchResponse.GetProperty("currentDatabase").GetString());

            using var missingApiResponse = await client.GetAsync("/api/does-not-exist");
            Assert.Equal(HttpStatusCode.NotFound, missingApiResponse.StatusCode);
            Assert.Equal("application/json; charset=utf-8", missingApiResponse.Content.Headers.ContentType?.ToString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
            DeleteFileIfExists(sqlitePath);
        }
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string url, object payload)
    {
        using var response = await client.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<(HttpStatusCode StatusCode, JsonElement Payload)> PostToolJsonAsync(
        HttpClient client,
        string url,
        object payload,
        bool includeMarker = true,
        string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        if (includeMarker)
        {
            request.Headers.Add("X-DatabaseMcp-Web", "1");
        }
        if (origin != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class TestBrowserLauncher : ICliBrowserLauncher
    {
        public bool TryOpen(Uri uri, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }
}
