using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tests;

internal static class ToolResultAssertions
{
    public static async Task<string> Json(this Task<CallToolResult> request)
    {
        var result = await request;
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
