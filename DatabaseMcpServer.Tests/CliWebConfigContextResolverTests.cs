using DatabaseMcpServer.Web;

namespace DatabaseMcpServer.Tests;

public class CliWebConfigContextResolverTests
{
    [Fact]
    public void Resolve_ShouldUseExplicitPathEvenWhenFileIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbmcp-web-{Guid.NewGuid():N}.json");

        var result = CliWebConfigContextResolver.Resolve(path);

        Assert.Equal(Path.GetFullPath(path), result.ConfigPath);
        Assert.Equal("--config", result.Source);
        Assert.False(result.ConfigExists);
    }
}
