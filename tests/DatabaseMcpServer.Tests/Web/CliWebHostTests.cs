using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DatabaseMcpServer.Web;

namespace DatabaseMcpServer.Tests;

public class CliWebHostTests
{
    [Theory]
    [InlineData("GET", "/api/config/export")]
    [InlineData("HEAD", "/api/config/export")]
    [InlineData("GET", "/api/dashboard")]
    [InlineData("HEAD", "/api/dashboard")]
    [InlineData("GET", "/api/tools")]
    [InlineData("GET", "/api/missing")]
    public async Task ReadGuards_RejectForeignHosts(string method, string path)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = "attacker.example";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.DoesNotContain("connectionString", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("foreign-origin", false)]
    [InlineData("cross-site", false)]
    [InlineData("same-site", false)]
    [InlineData("same-origin", true)]
    [InlineData("direct", true)]
    [InlineData("no-metadata", true)]
    public async Task ConfigurationExport_EnforcesOriginAndFetchMetadata(string scenario, bool allowed)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/config/export");
        if (scenario == "foreign-origin") request.Headers.Add("Origin", "http://attacker.example");
        if (scenario == "same-origin") request.Headers.Add("Origin", handle.BaseAddress.GetLeftPart(UriPartial.Authority));
        if (scenario is "cross-site" or "same-site" or "same-origin") request.Headers.Add("Sec-Fetch-Site", scenario);
        if (scenario == "direct") request.Headers.Add("Sec-Fetch-Site", "none");

        using var response = await client.SendAsync(request);

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (allowed)
        {
            Assert.Equal(File.ReadAllBytes(fixture.Path), await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        }
        else
        {
            Assert.Null(response.Content.Headers.ContentDisposition);
            Assert.DoesNotContain("connectionString", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("content-type", HttpStatusCode.UnsupportedMediaType, "Tool 请求必须使用 application/json。")]
    [InlineData("malformed-json", HttpStatusCode.BadRequest, "Tool 请求 JSON 无效:")]
    [InlineData("null-json", HttpStatusCode.BadRequest, "请求体不能为空。")]
    [InlineData("unknown-tool", HttpStatusCode.NotFound, "未知 tool 'missing_web_tool'。")]
    public async Task ToolInvocation_MapsRequestErrors(string scenario, HttpStatusCode expectedStatus, string expectedMessage)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        var body = scenario switch { "malformed-json" => "{", "null-json" => "null", _ => "{}" };
        var tool = scenario == "unknown-tool" ? "missing_web_tool" : "list_databases";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tools/{tool}/invoke")
        {
            Content = new StringContent(body, Encoding.UTF8, scenario == "content-type" ? "text/plain" : "application/json")
        };
        request.Headers.Add("X-DatabaseMcp-Web", "1");

        using var response = await client.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(expectedMessage, payload.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task ToolInvocation_EnforcesBodyLimitForFixedAndChunkedRequests(bool chunked, int offset)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        var body = Encoding.UTF8.GetBytes("{\"arguments\":{}}".PadRight(1024 * 1024 + offset));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tools/list_databases/invoke")
        {
            Version = HttpVersion.Version11,
            Content = chunked ? new ChunkedJsonContent(body) : new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TransferEncodingChunked = chunked;
        request.Headers.Add("X-DatabaseMcp-Web", "1");

        using var response = await client.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(offset > 0 ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(offset <= 0, payload.RootElement.GetProperty("success").GetBoolean());
        if (offset > 0)
        {
            Assert.Equal("Tool 请求体不能超过 1 MiB。", payload.RootElement.GetProperty("message").GetString());
        }
        else
        {
            Assert.True(payload.RootElement.GetProperty("toolSuccess").GetBoolean());
            Assert.Equal("list_databases", payload.RootElement.GetProperty("toolName").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("result").GetProperty("totalDatabases").GetInt32());
        }
    }

    [Fact]
    public async Task ConfigurationTransfer_RepairsInvalidConfigAndExportsSavedBytes()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        File.WriteAllText(fixture.Path, "{");
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        var context = await client.GetFromJsonAsync<JsonElement>("/api/context");
        Assert.True(context.GetProperty("success").GetBoolean());
        var invalidDashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.False(invalidDashboard.GetProperty("success").GetBoolean());
        const string imported = """{"databases":[{"name":"restored","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true}]}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/config/import")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(imported, Encoding.UTF8, "application/json"), "file", "restored.json" },
                { new StringContent("true"), "force" }
            }
        };
        request.Headers.Add("X-DatabaseMcp-Web", "1");

        using var response = await client.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("saved").GetBoolean());
        Assert.True(payload.RootElement.GetProperty("runtimeApplied").GetBoolean());
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.True(dashboard.GetProperty("success").GetBoolean());
        Assert.Equal("restored", dashboard.GetProperty("currentDatabase").GetString());
        using var export = await client.GetAsync("/api/config/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/json", export.Content.Headers.ContentType?.ToString());
        var disposition = export.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Matches(@"^databases-\d{8}-\d{6}\.json$", disposition.FileNameStar ?? disposition.FileName!.Trim('"'));
        Assert.Equal(File.ReadAllBytes(fixture.Path), await export.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_RejectsMissingOrEmptyUploadWithoutChangingConfig(bool emptyFile)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var original = File.ReadAllBytes(fixture.Path);
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        using var form = new MultipartFormDataContent { { new StringContent("true"), "force" } };
        if (emptyFile) form.Add(new ByteArrayContent([]), "file", "empty.json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/config/import") { Content = form };
        request.Headers.Add("X-DatabaseMcp-Web", "1");

        using var response = await client.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("请选择要导入的 JSON 文件。", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal(original, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public async Task StaticFilesAndFallback_KeepAssetsPagesAndApiResponsesDistinct()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        await using var handle = await new CliWebHost(new TestBrowserLauncher(), fixture.StatePath)
            .StartAsync(new CliWebCommandOptions(fixture.Path, null, false, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        var index = await client.GetStringAsync("/");
        using var page = await client.GetAsync("/connections/details");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType?.ToString());
        Assert.Equal(index, await page.Content.ReadAsStringAsync());
        var assetPath = Regex.Match(index, "src=\"(?<path>/assets/[^\"]+\\.js)\"");
        Assert.True(assetPath.Success, "The embedded index must reference its JavaScript asset.");
        using var asset = await client.GetAsync(assetPath.Groups["path"].Value);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains(asset.Content.Headers.ContentType?.MediaType, new[] { "text/javascript", "application/javascript" });
        Assert.NotEmpty(await asset.Content.ReadAsByteArrayAsync());
        using var missingApi = await client.GetAsync("/api/missing-host-route");
        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
        Assert.Equal("application/json; charset=utf-8", missingApi.Content.Headers.ContentType?.ToString());
        using var error = JsonDocument.Parse(await missingApi.Content.ReadAsStringAsync());
        Assert.False(error.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("未知 API: /api/missing-host-route", error.RootElement.GetProperty("message").GetString());
    }

    private sealed class ChunkedJsonContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (var offset = 0; offset < bytes.Length; offset += 8192)
            {
                await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(8192, bytes.Length - offset)));
            }
        }
    }

    [Fact]
    public async Task WriteGuards_RejectRequestsBeforeChangingConfiguration()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var host = new CliWebHost(new TestBrowserLauncher(), fixture.StatePath);
        await using var handle = await host.StartAsync(new CliWebCommandOptions(fixture.Path, null, false));
        using var client = new HttpClient { BaseAddress = handle.BaseAddress };
        var original = File.ReadAllText(fixture.Path);
        foreach (var (method, url, marker, origin, hostHeader) in new[]
        {
            (HttpMethod.Post, "/api/databases/analytics/set-default", false, (string?)null, (string?)null),
            (HttpMethod.Delete, "/api/databases/primary", true, "http://external.test", null),
            (HttpMethod.Post, "/api/config/import", true, "null", null),
            (HttpMethod.Post, "/api/databases/analytics/set-default", true, null, "external.test"),
            (HttpMethod.Put, "/api/databases/primary", false, null, null),
            (HttpMethod.Patch, "/api/databases/primary", false, null, null)
        })
        {
            using var request = new HttpRequestMessage(method, url);
            if (marker) request.Headers.Add("X-DatabaseMcp-Web", "1");
            if (origin != null) request.Headers.TryAddWithoutValidation("Origin", origin);
            if (hostHeader != null) request.Headers.Host = hostHeader;
            if (url.EndsWith("import", StringComparison.Ordinal))
                request.Content = new MultipartFormDataContent { { new StringContent("{}"), "file", "invalid.json" } };
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(original, File.ReadAllText(fixture.Path));
            Assert.False(File.Exists(fixture.StatePath));
        }
        using var allowed = new HttpRequestMessage(HttpMethod.Post, "/api/databases/analytics/set-default");
        allowed.Headers.Add("X-DatabaseMcp-Web", "1");
        allowed.Headers.Add("Origin", handle.BaseAddress.GetLeftPart(UriPartial.Authority));
        using var success = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using var payload = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEqual(original, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public async Task TwoWebHosts_KeepIndependentPathsAndSelections()
    {
        using var first = new RuntimeConsistencyTests.Fixture();
        using var second = new RuntimeConsistencyTests.Fixture();
        var environmentBefore = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        await using var one = await new CliWebHost(new TestBrowserLauncher(), first.StatePath)
            .StartAsync(new CliWebCommandOptions(first.Path, null, false));
        await using var two = await new CliWebHost(new TestBrowserLauncher(), second.StatePath)
            .StartAsync(new CliWebCommandOptions(second.Path, null, false));
        using var clientOne = new HttpClient { BaseAddress = one.BaseAddress };
        using var clientTwo = new HttpClient { BaseAddress = two.BaseAddress };
        await PostJsonAsync(clientOne, "/api/current-database/switch", new { databaseName = "analytics" });
        var renamed = await PostJsonAsync(clientOne, "/api/databases/analytics/rename", new { newName = "reporting" });
        Assert.True(renamed.GetProperty("success").GetBoolean());
        var currentOne = await PostToolJsonAsync(clientOne, "/api/tools/get_current_database/invoke", new { arguments = new { } });
        var currentTwo = await PostToolJsonAsync(clientTwo, "/api/tools/get_current_database/invoke", new { arguments = new { } });
        Assert.Equal("reporting", currentOne.Payload.GetProperty("result").GetProperty("currentDatabase").GetString());
        Assert.Equal("primary", currentTwo.Payload.GetProperty("result").GetProperty("currentDatabase").GetString());
        Assert.DoesNotContain("reporting", File.ReadAllText(second.Path));
        Assert.Equal(environmentBefore, Environment.GetEnvironmentVariable("DB_CONFIG_PATH"));
    }

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
            Assert.True(safeInvocation.Payload.GetProperty("queueMs").GetInt64() >= 0);
            Assert.True(safeInvocation.Payload.GetProperty("durationMs").GetInt64() >= 0);

            var failedQuery = await PostToolJsonAsync(client, "/api/tools/query_database/invoke", new
            {
                arguments = new Dictionary<string, object> { ["sql"] = "select * from missing_table_for_test" }
            });
            Assert.Equal(HttpStatusCode.OK, failedQuery.StatusCode);
            Assert.True(failedQuery.Payload.GetProperty("success").GetBoolean());
            Assert.False(failedQuery.Payload.GetProperty("toolSuccess").GetBoolean());
            Assert.False(failedQuery.Payload.GetProperty("result").GetProperty("success").GetBoolean());

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
                    ["columns"] = new[] { new { name = "id", dataType = "integer", primaryKey = true } }
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
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-DatabaseMcp-Web", "1");
        using var response = await client.SendAsync(request);
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
