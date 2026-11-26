using DatabaseMcpServer.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DatabaseMcpServer.Tests;

public class LocalWebRequestGuardTests
{
    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    public void MarkerRequirement_DependsOnRequestMethod(string method, bool required)
    {
        var request = Request("localhost", null);
        request.Method = method;
        request.Headers.Remove("X-DatabaseMcp-Web");
        Assert.Equal(required ? "缺少本地 Web 请求标记。" : null, LocalWebRequestGuard.Validate(request));
    }

    [Theory]
    [InlineData("localhost:8080", "http://localhost:8080")]
    [InlineData("127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("[::1]:8080", "http://[::1]:8080")]
    [InlineData("localhost", "http://localhost:80")]
    [InlineData("localhost", null)]
    public void LocalSameOrigin_IsAccepted(string host, string? origin)
    {
        var request = Request(host, origin);
        Assert.Null(LocalWebRequestGuard.Validate(request));
    }

    [Theory]
    [InlineData("localhost:8080", "http://localhost:8081")]
    [InlineData("localhost:8080", "https://localhost:8080")]
    [InlineData("localhost:8080", "http://localhost.attacker.test:8080")]
    [InlineData("attacker.test", "http://attacker.test")]
    [InlineData("localhost", "null")]
    [InlineData("localhost", "http://localhost/path")]
    public void UnsafeOriginOrHost_IsRejected(string host, string origin)
        => Assert.NotNull(LocalWebRequestGuard.Validate(Request(host, origin)));

    [Fact]
    public void MissingMarkerAndMultipleOrigins_AreRejected()
    {
        var request = Request("localhost", null);
        request.Headers.Remove("X-DatabaseMcp-Web");
        Assert.NotNull(LocalWebRequestGuard.Validate(request));
        request.Headers["X-DatabaseMcp-Web"] = "1";
        request.Headers.Origin = new StringValues(["http://localhost", "http://localhost"]);
        Assert.NotNull(LocalWebRequestGuard.Validate(request));
    }

    private static HttpRequest Request(string host, string? origin)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Post;
        request.Scheme = "http";
        request.Host = HostString.FromUriComponent(host);
        request.Headers["X-DatabaseMcp-Web"] = "1";
        if (origin != null) request.Headers.Origin = origin;
        return request;
    }
}
