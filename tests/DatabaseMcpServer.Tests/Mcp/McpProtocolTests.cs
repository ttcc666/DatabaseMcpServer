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
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task Tools_PublishCurrentSchemasAndQueryPayloadsOverProtocol(string? protocolVersion)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services => services.AddDatabaseMcpServer());
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
            Assert.Equal(62, tools.Count - 1);
            foreach (var name in new[] { "mongo_list_collections", "mongo_find", "mongo_count", "mongo_insert_one", "mongo_update", "mongo_delete" })
            {
                var mongo = Assert.Single(tools, t => t.Name == name);
                Assert.Equal("object", mongo.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
                if (name is "mongo_insert_one" or "mongo_update" or "mongo_delete") Assert.True(mongo.ProtocolTool.Annotations?.DestructiveHint);
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
            var missingMongoDocument = await client.CallToolAsync("mongo_insert_one", new Dictionary<string, object?> { ["collection"] = "docs" }, cancellationToken: timeout.Token);
            Assert.Equal(1004, missingMongoDocument.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
            Assert.Equal("not-executed", missingMongoDocument.StructuredContent.Value.GetProperty("writeOutcome").GetString());
            foreach (var arguments in new Dictionary<string, object?>[]
            {
                new() { ["document"] = new { x = 1 } },
                new() { ["collection"] = "docs", ["filter"] = new { x = 1 }, ["many"] = "yes" }
            })
            {
                var invalid = await client.CallToolAsync(arguments.ContainsKey("document") ? "mongo_insert_one" : "mongo_delete",
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
                ["queries"] = new[] { "select 1 as value", "select * from missing_table" }
            }, cancellationToken: timeout.Token);
            Assert.NotEqual(true, partial.IsError);
            using var partialPayload = JsonDocument.Parse(Assert.Single(partial.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(1, partialPayload.RootElement.GetProperty("failedQueries").GetInt32());

            // Saturate the real pool, then cancel a protocol call while it is waiting for a lease.
            var database = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Services.SqlDatabaseSessions>();
            var leases = Enumerable.Range(0, 8).Select(_ => database.AcquireClient()).ToArray();
            try
            {
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
