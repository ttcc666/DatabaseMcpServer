using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Tests;

public class DatabaseConfigMonitorSettingsTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public void GetEnvironmentOverride_ShouldParseKnownValues(string value, bool expected)
    {
        var original = Environment.GetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, value);
            Assert.Equal(expected, DatabaseConfigMonitorSettings.GetEnvironmentOverride());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, original);
        }
    }

    [Fact]
    public void IsEnabled_ShouldUseFileFlagWhenEnvironmentIsUnset()
    {
        var original = Environment.GetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, null);
            Assert.False(DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: false));
            Assert.True(DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, original);
        }
    }

    [Fact]
    public void IsEnabled_ShouldLetEnvironmentFalseDisableFileFlag()
    {
        var original = Environment.GetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, "false");
            Assert.False(DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, original);
        }
    }

    [Fact]
    public void IsEnabled_ShouldLetProcessOverrideWinOverEnvironmentAndFile()
    {
        var original = Environment.GetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, "false");
            Assert.True(DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: false, processOverride: true));
            Assert.False(DatabaseConfigMonitorSettings.IsEnabled(enableMonitorConfig: true, processOverride: false));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseConfigMonitorSettings.EnvironmentVariableName, original);
        }
    }
}
