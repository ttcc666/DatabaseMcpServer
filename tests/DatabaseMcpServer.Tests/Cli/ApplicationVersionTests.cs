using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Hosting;

namespace DatabaseMcpServer.Tests;

public class ApplicationVersionTests
{
    [Fact]
    public async Task Version_DoesNotRequireConfiguration()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await new CliRunner().RunAsync(["--version"], output, error));
        Assert.Equal(ApplicationVersion.Value + Environment.NewLine, output.ToString());
        Assert.Matches("^5\\.0\\.0(?:-[a-zA-Z0-9.-]+)?$", output.ToString().Trim());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Version_RejectsTrailingOptions()
        => Assert.Equal(CliCommandKind.Error, new CliCommandParser(new()).Parse(["--version", "--config", "missing"]).Kind);
}
