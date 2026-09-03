using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class DatabaseStartupArgumentsTests
{
    [Fact]
    public void Parse_ShouldStartMcpServer_WhenArgsAreEmpty()
    {
        var result = DatabaseStartupArguments.Parse([]);

        Assert.True(result.RunMcpServer);
        Assert.Null(result.EnableMonitorConfig);
        Assert.Empty(result.RemainingArgs);
    }

    [Fact]
    public void Parse_ShouldStartMcpServer_WhenOnlyEnableMonitorConfigFlagIsPresent()
    {
        var result = DatabaseStartupArguments.Parse(["--enable-monitor-config"]);

        Assert.True(result.RunMcpServer);
        Assert.True(result.EnableMonitorConfig);
        Assert.Empty(result.RemainingArgs);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Parse_ShouldReadExplicitBooleanValue(string rawValue, bool expected)
    {
        var result = DatabaseStartupArguments.Parse(["--enable-monitor-config", rawValue]);

        Assert.True(result.RunMcpServer);
        Assert.Equal(expected, result.EnableMonitorConfig);
    }

    [Fact]
    public void Parse_ShouldReadEqualsBooleanValue()
    {
        var result = DatabaseStartupArguments.Parse(["--enable-monitor-config=false"]);

        Assert.True(result.RunMcpServer);
        Assert.False(result.EnableMonitorConfig);
    }

    [Fact]
    public void Parse_ShouldLeaveSubcommandAfterGlobalFlag()
    {
        var result = DatabaseStartupArguments.Parse(["--enable-monitor-config", "true", "-web", "--no-browser"]);

        Assert.False(result.RunMcpServer);
        Assert.True(result.EnableMonitorConfig);
        Assert.Equal(["-web", "--no-browser"], result.RemainingArgs);
    }

    [Fact]
    public void Parse_ShouldNotConsumeLaterSubcommandTokens()
    {
        var result = DatabaseStartupArguments.Parse(["-web", "--enable-monitor-config", "true"]);

        Assert.Null(result.EnableMonitorConfig);
        Assert.Equal(["-web", "--enable-monitor-config", "true"], result.RemainingArgs);
    }
}
