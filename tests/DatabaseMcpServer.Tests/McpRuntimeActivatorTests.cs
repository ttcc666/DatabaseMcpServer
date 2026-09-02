using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Gui.Core.Services;

namespace DatabaseMcpServer.Tests;

public sealed class McpRuntimeActivatorTests
{
    [Fact]
    public async Task TrySwitchDatabaseAsync_ShouldBeNoOp_WhenWebUrlIsNotConfigured()
    {
        var previous = Environment.GetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName);
        Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, null);
        try
        {
            var result = await new McpRuntimeActivator().TrySwitchDatabaseAsync("primary");

            Assert.False(result.Attempted);
            Assert.False(result.Succeeded);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, previous);
        }
    }

    [Fact]
    public async Task TrySwitchDatabaseAsync_ShouldPostDatabaseName_AndAcceptSuccessResponse()
    {
        var handler = new RecordingHandler("{\"success\":true,\"message\":\"已成功切换\"}");
        var previous = Environment.GetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName);
        Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, "http://127.0.0.1:5100/");
        try
        {
            var result = await new McpRuntimeActivator(handler).TrySwitchDatabaseAsync("primary");

            Assert.True(result.Attempted);
            Assert.True(result.Succeeded);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("http://127.0.0.1:5100/api/current-database/switch", handler.RequestUri?.ToString());
            using var body = JsonDocument.Parse(handler.Body!);
            Assert.Equal("primary", body.RootElement.GetProperty("databaseName").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, previous);
        }
    }

    [Fact]
    public async Task TrySwitchDatabaseAsync_ShouldDegradeToFailure_WhenRuntimeRejectsRequest()
    {
        var handler = new RecordingHandler("{\"success\":false,\"message\":\"连接不存在\"}");
        var previous = Environment.GetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName);
        Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, "http://127.0.0.1:5100");
        try
        {
            var result = await new McpRuntimeActivator(handler).TrySwitchDatabaseAsync("missing");

            Assert.True(result.Attempted);
            Assert.False(result.Succeeded);
            Assert.Contains("连接不存在", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpRuntimeActivator.WebUrlEnvironmentVariableName, previous);
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
