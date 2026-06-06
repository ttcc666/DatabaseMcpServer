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
            Assert.Contains("DatabaseMcpServer Web Config", rootResponse, StringComparison.Ordinal);

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
