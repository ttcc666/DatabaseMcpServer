using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Configuration;
using System.IO.Pipelines;
using System.Text.Json;
using DatabaseMcpServer.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tests;

public class McpProtocolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public async Task Completion_ReportsFullMatchCountAndTruncation(int count)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services => services.AddDatabaseMcpServer());
        var configuration = new ConfigurationFileStore().Load(fixture.Path);
        var expected = Enumerable.Range(0, count).Select(i => $"match{i:D3}").ToArray();
        foreach (var name in expected)
            configuration.Databases.Add(new() { Name = name, DbType = "Sqlite", ConnectionString = "Data Source=:memory:" });
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(configuration));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var outgoing = new Pipe();
        var incoming = new Pipe();
        await using var server = McpServer.Create(new StreamServerTransport(outgoing.Reader.AsStream(), incoming.Writer.AsStream()),
            fixture.Host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value, serviceProvider: fixture.Host.Services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = server.RunAsync(timeout.Token);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(outgoing.Writer.AsStream(), incoming.Reader.AsStream()), cancellationToken: timeout.Token))
        {
            var result = await client.CompleteAsync(new PromptReference { Name = "analyze_slow_query" }, "databaseName", "MATCH", cancellationToken: timeout.Token);
            Assert.Equal(expected.Take(100), result.Completion.Values);
            Assert.Equal(count, result.Completion.Total);
            Assert.Equal(count > 100, result.Completion.HasMore);
        }
        await server.DisposeAsync();
        await run;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task Tools_PublishCurrentSchemasAndQueryPayloadsOverProtocol(string? protocolVersion)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services => services.AddDatabaseMcpServer());
        var configuration = new ConfigurationFileStore().Load(fixture.Path);
        configuration.Databases[0].ConnectionString = $"Data Source={Path.Combine(fixture.Directory.FullName, "protocol.db")};Pooling=False";
        configuration.Databases.Add(new() { Name = "mongo", DbType = "MongoDb", ConnectionString = "mongodb://localhost:1/test" });
        configuration.Databases.Add(new() { Name = "sqlserver", DbType = "SqlServer", ConnectionString = "Server=localhost,1;Database=test;User ID=test;Password=unused;TrustServerCertificate=true" });
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(configuration));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var outgoing = new Pipe();
        var incoming = new Pipe();
        var options = fixture.Host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCancelled = false;
        options.ToolCollection!.Add(McpServerTool.Create(async (CancellationToken token) =>
        {
            entered.TrySetResult();
            try
            {
                return await fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Query.DatabaseQueryTools>()
                    .QueryDatabase("select 9 as value", cancellationToken: token);
            }
            finally { serverCancelled = token.IsCancellationRequested; finished.TrySetResult(); }
        }, new() { Name = "cancel_query" }));
        await using var server = McpServer.Create(new StreamServerTransport(outgoing.Reader.AsStream(), incoming.Writer.AsStream()),
            options, serviceProvider: fixture.Host.Services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = server.RunAsync(timeout.Token);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(outgoing.Writer.AsStream(), incoming.Reader.AsStream()),
            new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: timeout.Token))
        {
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.False(string.IsNullOrWhiteSpace(client.ServerInstructions));
            Assert.NotNull(client.ServerCapabilities.Resources);
            Assert.NotNull(client.ServerCapabilities.Prompts);
            Assert.NotNull(client.ServerCapabilities.Completions);
            Assert.All(tools.Where(t => t.Name != "cancel_query"), t => Assert.False(string.IsNullOrWhiteSpace(t.ProtocolTool.Title), t.Name));

            var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
            Assert.Contains(resources, r => r.Uri == "db://connections");
            var connectionsResource = await client.ReadResourceAsync("db://connections", cancellationToken: timeout.Token);
            using (var connJson = JsonDocument.Parse(((TextResourceContents)Assert.Single(connectionsResource.Contents)).Text))
                Assert.True(connJson.RootElement.GetProperty("success").GetBoolean());

            var templates = await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token);
            Assert.Contains(templates, t => t.UriTemplate == "db://capabilities/{databaseName}");
            Assert.Contains(templates, t => t.UriTemplate == "db://schema/{databaseName}/{tableName}");
            var capsResource = await client.ReadResourceAsync("db://capabilities/analytics", cancellationToken: timeout.Token);
            using (var capJson = JsonDocument.Parse(((TextResourceContents)Assert.Single(capsResource.Contents)).Text))
                Assert.Equal("analytics", capJson.RootElement.GetProperty("databaseName").GetString());

            var prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
            Assert.Equal(["analyze_slow_query", "safe_schema_migration"],
                prompts.Select(p => p.Name).Order().ToArray());
            var promptResult = await client.GetPromptAsync("analyze_slow_query",
                new Dictionary<string, object?> { ["sql"] = "SELECT 1", ["databaseName"] = "analytics" },
                cancellationToken: timeout.Token);
            Assert.NotEmpty(promptResult.Messages);

            var completion = await client.CompleteAsync(
                new PromptReference { Name = "analyze_slow_query" },
                "databaseName", "ana",
                cancellationToken: timeout.Token);
            Assert.Contains("analytics", completion.Completion.Values);
            var defaultBudget = await client.CallToolAsync("get_data_set_all", new Dictionary<string, object?>
            {
                ["sql"] = "WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v+1 FROM n WHERE v<1002) SELECT v FROM n"
            }, cancellationToken: timeout.Token);
            Assert.False(defaultBudget.IsError == true);
            using (var budgetPayload = JsonDocument.Parse(Assert.Single(defaultBudget.Content.OfType<TextContentBlock>()).Text))
            {
                Assert.Equal(1000, budgetPayload.RootElement.GetProperty("rowCount").GetInt32());
                Assert.True(budgetPayload.RootElement.GetProperty("limitReached").GetBoolean());
            }
            var invalidSections = await client.CallToolAsync("get_table_schema", new Dictionary<string, object?> { ["sections"] = new[] { "Columns" } }, cancellationToken: timeout.Token);
            Assert.True(invalidSections.IsError);
            Assert.Equal(1004, invalidSections.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            Assert.Equal(ToolContract.Names, tools.Where(t => t.Name != "cancel_query").Select(t => t.Name).Order());
            var tableQuery = tools.Single(t => t.Name == "query_table_page").ProtocolTool;
            var queryProperties = tableQuery.InputSchema.GetProperty("properties");
            Assert.Equal(new[] { "eq", "ne", "gt", "gte", "lt", "lte", "isNull", "isNotNull" },
                queryProperties.GetProperty("filters").GetProperty("items").GetProperty("properties").GetProperty("op").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(new[] { "asc", "desc" },
                queryProperties.GetProperty("orderBy").GetProperty("items").GetProperty("properties").GetProperty("direction").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
            var insertTool = tools.Single(t => t.Name == "insert_row").ProtocolTool;
            Assert.True(insertTool.Annotations!.DestructiveHint);
            Assert.Equal("object", insertTool.InputSchema.GetProperty("properties").GetProperty("row").GetProperty("type").GetString());
            using (var db = fixture.Sessions.AcquireClient())
                db.Client.Ado.ExecuteCommand("CREATE TABLE protocol_items(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)");
            var inserted = await client.CallToolAsync("insert_row", new Dictionary<string, object?>
            {
                ["tableName"] = "protocol_items", ["row"] = new { name = "from MCP" }
            }, cancellationToken: timeout.Token);
            Assert.False(inserted.IsError == true, Assert.Single(inserted.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal("1", DatabaseRoleTests.Success(inserted).GetProperty("identity").GetString());
            var selected = await client.CallToolAsync("query_table_page", new Dictionary<string, object?>
            {
                ["tableName"] = "protocol_items", ["columns"] = new[] { "name" },
                ["filters"] = new[] { new { column = "id", op = "gte", value = 1 } },
                ["orderBy"] = new[] { new { column = "id", direction = "desc" } }
            }, cancellationToken: timeout.Token);
            Assert.False(selected.IsError);
            Assert.Equal("from MCP", selected.StructuredContent!.Value.GetProperty("data")[0].GetProperty("name").GetString());
            Assert.Equal("offset", selected.StructuredContent.Value.GetProperty("pagingMode").GetString());
            var capability = tools.Single(t => t.Name == "get_database_capabilities").ProtocolTool;
            Assert.Equal(new[] { "databaseName" }, capability.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
            var catalog = fixture.Host.Services.GetRequiredService<ToolCatalog>();
            var web = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Web.CliWebToolService>();
            var webCatalog = JsonSerializer.SerializeToElement(web.GetTools()).GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
            foreach (var name in ToolContract.RemovedNames)
            {
                Assert.False(webCatalog.ContainsKey(name));
                await Assert.ThrowsAnyAsync<ModelContextProtocol.McpException>(async () => await client.CallToolAsync(name, cancellationToken: timeout.Token));
                await Assert.ThrowsAsync<KeyNotFoundException>(() => web.InvokeAsync(name, new(null, null), timeout.Token));
            }
            foreach (var descriptor in catalog.Tools)
            {
                var actual = tools.Single(t => t.Name == descriptor.Name).ProtocolTool;
                Assert.True(JsonElement.DeepEquals(descriptor.ProtocolTool.InputSchema, actual.InputSchema));
                Assert.True(JsonElement.DeepEquals(webCatalog[descriptor.Name].GetProperty("inputSchema"), actual.InputSchema));
                Assert.Equal(descriptor.RequiredAccessName, webCatalog[descriptor.Name].GetProperty("requiredAccess").GetString());
            }
            foreach (var (name, arguments) in new (string, Dictionary<string, object?>)[]
            {
                ("list_objects", new() { ["kind"] = "table" }),
                ("get_object_definition", new() { ["kind"] = "view", ["name"] = "missing" }),
                ("explain_query", new() { ["sql"] = "select 1" })
            })
            {
                var response = await client.CallToolAsync(name, arguments, cancellationToken: timeout.Token);
                Assert.False(response.IsError);
                Assert.True(JsonElement.DeepEquals(response.StructuredContent!.Value,
                    JsonSerializer.Deserialize<JsonElement>(Assert.Single(response.Content.OfType<TextContentBlock>()).Text)));
                var webResponse = JsonSerializer.SerializeToElement(await web.InvokeAsync(name,
                    new(arguments.ToDictionary(p => ToolCatalog.ToKebabCase(p.Key), p => JsonSerializer.SerializeToElement(p.Value)), null), timeout.Token));
                Assert.True(webResponse.GetProperty("toolSuccess").GetBoolean());
                Assert.True(JsonElement.DeepEquals(response.StructuredContent.Value, webResponse.GetProperty("result")));
            }
            foreach (var raw in new[] { "null", "{}", "[null]", "[{\"$match\":{},\"$match\":{}}]" })
            {
                var invalid = await client.CallToolAsync("mongo_aggregate", new Dictionary<string, object?>
                { ["collection"] = "docs", ["pipeline"] = JsonSerializer.Deserialize<JsonElement>(raw) }, cancellationToken: timeout.Token);
                Assert.True(invalid.IsError);
                Assert.Equal(1004, invalid.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            }
            foreach (var name in new[] { "mongo_find", "mongo_aggregate", "mongo_insert_many", "mongo_update", "mongo_delete", "mongo_truncate" })
            {
                var mongo = Assert.Single(tools, t => t.Name == name);
                Assert.Equal("object", mongo.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
                if (name is "mongo_insert_many" or "mongo_update" or "mongo_delete" or "mongo_truncate") Assert.True(mongo.ProtocolTool.Annotations?.DestructiveHint);
                else Assert.True(mongo.ProtocolTool.Annotations?.ReadOnlyHint);
            }
            var mongoFind = tools.Single(t => t.Name == "mongo_find");
            Assert.Equal("object", mongoFind.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("filter").GetProperty("type").GetString());
            foreach (var raw in new[] { "null", "[]", "\"{}\"", "{\"x\":1,\"x\":2}" })
            {
                var invalid = await mongoFind.CallAsync(new Dictionary<string, object?>
                { ["collection"] = "docs", ["filter"] = JsonSerializer.Deserialize<JsonElement>(raw) }, cancellationToken: timeout.Token);
                Assert.True(invalid.IsError);
                Assert.Equal(1004, invalid.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
                using var textResult = JsonDocument.Parse(Assert.Single(invalid.Content.OfType<TextContentBlock>()).Text);
                Assert.True(JsonElement.DeepEquals(textResult.RootElement, invalid.StructuredContent.Value));
            }
            var unsupportedMongo = await mongoFind.CallAsync(new Dictionary<string, object?> { ["collection"] = "docs" }, cancellationToken: timeout.Token);
            Assert.True(unsupportedMongo.IsError);
            Assert.Equal(1012, unsupportedMongo.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            var missingMongoDocument = await client.CallToolAsync("mongo_insert_many", new Dictionary<string, object?> { ["collection"] = "docs" }, cancellationToken: timeout.Token);
            Assert.Equal(1004, missingMongoDocument.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            Assert.Equal("not-executed", missingMongoDocument.StructuredContent.Value.GetProperty("writeOutcome").GetString());
            foreach (var arguments in new Dictionary<string, object?>[]
            {
                new() { ["documents"] = new[] { new { x = 1 } } },
                new() { ["collection"] = "docs", ["filter"] = new { x = 1 }, ["allowAll"] = "yes" }
            })
            {
                var invalid = await client.CallToolAsync(arguments.ContainsKey("documents") ? "mongo_insert_many" : "mongo_delete",
                    arguments, cancellationToken: timeout.Token);
                Assert.True(invalid.IsError);
                Assert.Equal(1004, invalid.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
                Assert.Equal("not-executed", invalid.StructuredContent.Value.GetProperty("writeOutcome").GetString());
            }
            Assert.Equal(new DatabaseMcpServer.Tools.ToolCatalog().Tools.Select(tool => tool.Name).Order(),
                tools.Where(tool => tool.Name != "cancel_query").Select(tool => tool.Name).Order());
            Assert.All(tools, tool => Assert.DoesNotContain("cancellationToken",
                tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)));
            Assert.True(Assert.Single(tools, tool => tool.Name == "execute_command").ProtocolTool.Annotations?.DestructiveHint);
            foreach (var name in new[] { "reload_database_config", "switch_database" })
            {
                var annotations = Assert.Single(tools, tool => tool.Name == name).ProtocolTool.Annotations!;
                Assert.True(annotations.DestructiveHint);
                Assert.False(annotations.ReadOnlyHint ?? false);
                Assert.True(annotations.IdempotentHint);
            }
            var query = Assert.Single(tools, tool => tool.Name == "query_database");
            Assert.True(query.ProtocolTool.Annotations?.ReadOnlyHint);
            var schema = query.ProtocolTool.InputSchema;
            Assert.Equal(new[] { "commandTimeoutSeconds", "databaseName", "maxResponseBytes", "maxRows", "parameters", "sql" },
                schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal("sql", Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString());
            Assert.DoesNotContain(tools, tool => tool.Name is "sql_query" or "sql_query_with_in_parameter" or "call_stored_procedure_with_output");
            var structuredTool = query;
            Assert.Equal("array", schema.GetProperty("properties").GetProperty("parameters").GetProperty("type")[0].GetString());
            foreach (var (name, arguments) in new (string, Dictionary<string, object?>)[]
            {
                ("query_database", new() { ["sql"] = "select @v", ["parameters"] = "{\"v\":1}" }),
                ("execute_command", new() { ["sql"] = "select 1", ["typedParameters"] = Array.Empty<object>() }),
                ("call_stored_procedure", new() { ["procedureName"] = "unused", ["outputParameters"] = "[\"v\"]" }),
                ("batch_execute_commands", new() { ["commands"] = new[] { "select 1" } }),
                ("batch_execute_commands", new() { ["commands"] = "[{\"sql\":\"select 1\"}]" })
            })
            {
                var rejected = await client.CallToolAsync(name, arguments, cancellationToken: timeout.Token);
                Assert.True(rejected.IsError, $"{name} accepted an obsolete argument shape.");
            }
            var outputSchema = structuredTool.ProtocolTool.OutputSchema!.Value;
            Assert.Equal("object", outputSchema.GetProperty("type").GetString());
            Assert.True(outputSchema.GetProperty("properties").TryGetProperty("configurationVersion", out _));
            var structured = await structuredTool.CallAsync(new Dictionary<string, object?>
            {
                ["sql"] = "select @v as v", ["databaseName"] = "analytics",
                ["parameters"] = new[] { new { name = "v", value = (object?)null, dbType = "Int64" } }
            }, cancellationToken: timeout.Token);
            Assert.False(structured.IsError);
            var structuredData = structured.StructuredContent!.Value;
            Assert.Equal("analytics", structuredData.GetProperty("databaseName").GetString());
            Assert.Equal(JsonValueKind.Null, structuredData.GetProperty("data")[0].GetProperty("v").ValueKind);
            using var structuredText = JsonDocument.Parse(Assert.Single(structured.Content.OfType<TextContentBlock>()).Text);
            Assert.True(JsonElement.DeepEquals(structuredText.RootElement, structuredData));
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(structured).Length > System.Text.Encoding.UTF8.GetByteCount(structuredText.RootElement.GetRawText()));
            var structuredError = await structuredTool.CallAsync(new Dictionary<string, object?> { ["sql"] = "drop table blocked" }, cancellationToken: timeout.Token);
            Assert.True(structuredError.IsError);
            Assert.Equal(1003, structuredError.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
            config.Databases.Single(d => d.Name == "analytics").AccessMode = "read-only";
            File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
            Assert.True(fixture.Service.ReloadConfiguration().Success);
            var denied = await client.CallToolAsync("execute_command", new Dictionary<string, object?>
            { ["sql"] = "insert into anything values (1)", ["databaseName"] = "analytics" }, cancellationToken: timeout.Token);
            Assert.True(denied.IsError);
            using var deniedPayload = JsonDocument.Parse(Assert.Single(denied.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(1010, deniedPayload.RootElement.GetProperty("errorCode").GetInt32());
            var result = await query.CallAsync(new Dictionary<string, object?> { ["sql"] = "select 7 as value" }, cancellationToken: timeout.Token);
            Assert.NotEqual(true, result.IsError);
            using var payload = JsonDocument.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
            Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(7, payload.RootElement.GetProperty("data")[0].GetProperty("value").GetInt32());
            var failure = await query.CallAsync(new Dictionary<string, object?> { ["sql"] = "drop table forbidden" }, cancellationToken: timeout.Token);
            Assert.True(failure.IsError);
            using var error = JsonDocument.Parse(Assert.Single(failure.Content.OfType<TextContentBlock>()).Text);
            Assert.False(error.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1003, error.RootElement.GetProperty("errorCode").GetInt32());

            var missingTable = await query.CallAsync(new Dictionary<string, object?> { ["sql"] = "select * from missing_table" }, cancellationToken: timeout.Token);
            Assert.True(missingTable.IsError);
            using var databaseError = JsonDocument.Parse(Assert.Single(missingTable.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal((int)DatabaseMcpServer.Models.DatabaseErrorCode.QueryExecutionFailed,
                databaseError.RootElement.GetProperty("errorCode").GetInt32());

            var batch = Assert.Single(tools, tool => tool.Name == "batch_sql_query");
            var partial = await batch.CallAsync(new Dictionary<string, object?>
            {
                ["commands"] = new[] { new { sql = "select 1 as value" }, new { sql = "select * from missing_table" } }
            }, cancellationToken: timeout.Token);
            Assert.NotEqual(true, partial.IsError);
            using var partialPayload = JsonDocument.Parse(Assert.Single(partial.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(1, partialPayload.RootElement.GetProperty("failedQueries").GetInt32());

            // Saturate the real pool, then cancel a protocol call while it is waiting for a lease.
            var database = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Services.SqlDatabaseSessions>();
            var leases = Enumerable.Range(0, 8).Select(_ => database.AcquireClient()).ToArray();
            try
            {
                var batchTimeout = await client.CallToolAsync("batch_execute_commands", new Dictionary<string, object?>
                {
                    ["commands"] = new[] { new { sql = "insert into items values(1)" } },
                    ["commandTimeoutSeconds"] = 1
                }, cancellationToken: timeout.Token);
                Assert.True(batchTimeout.IsError);
                using var batchError = JsonDocument.Parse(Assert.Single(batchTimeout.Content.OfType<TextContentBlock>()).Text);
                Assert.Equal(1011, batchError.RootElement.GetProperty("errorCode").GetInt32());
                var progress = batchError.RootElement.GetProperty("data");
                Assert.Empty(progress.GetProperty("results").EnumerateArray());
                Assert.Equal(0, progress.GetProperty("notExecutedFromIndex").GetInt32());

                var requestId = new RequestId("cancel-query");
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                var waiting = client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call",
                    new() { Name = "cancel_query" }, requestId: requestId, cancellationToken: cancellation.Token).AsTask();
                await entered.Task.WaitAsync(timeout.Token);
                // The SDK must send notifications/cancelled when only the client token is cancelled.
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
                await finished.Task.WaitAsync(timeout.Token);
                Assert.True(serverCancelled);
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
            var afterCancellation = await query.CallAsync(new Dictionary<string, object?> { ["sql"] = "select 11 as value" }, cancellationToken: timeout.Token);
            using var nextPayload = JsonDocument.Parse(Assert.Single(afterCancellation.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(11, nextPayload.RootElement.GetProperty("data")[0].GetProperty("value").GetInt32());
        }
        await server.DisposeAsync();
        await run;
    }
}
