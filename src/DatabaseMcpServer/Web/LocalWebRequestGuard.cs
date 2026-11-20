using System.Net;
using Microsoft.AspNetCore.Http;

namespace DatabaseMcpServer.Web;

internal static class LocalWebRequestGuard
{
    public static string? Validate(HttpRequest request)
    {
        var host = request.Host.Host.Trim('[', ']');
        if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)))
            return "拒绝非本地 Host。";

        if (!request.Headers.TryGetValue("X-DatabaseMcp-Web", out var marker)
            || marker.Count != 1 || marker[0] != "1")
            return "缺少本地 Web 请求标记。";

        if (request.Headers.TryGetValue("Origin", out var origins))
        {
            if (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
                || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
                || origin.Query.Length != 0 || origin.Fragment.Length != 0)
                return "请求 Origin 无效。";
            if (!string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(origin.Host.Trim('[', ']'), host, StringComparison.OrdinalIgnoreCase)
                || origin.Port != (request.Host.Port ?? (request.IsHttps ? 443 : 80)))
                return "拒绝跨来源请求。";
        }
        return null;
    }
}
