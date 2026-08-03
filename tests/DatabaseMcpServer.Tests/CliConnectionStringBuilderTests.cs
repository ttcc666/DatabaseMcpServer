using System.Data.Common;
using DatabaseMcpServer.Web;

namespace DatabaseMcpServer.Tests;

public class CliConnectionStringBuilderTests
{
    private readonly CliConnectionStringBuilder _builder = new();

    [Fact]
    public void ProfileCatalog_ShouldRemoveSensitiveDefaults_AndFallbackForUnknownTypes()
    {
        var profile = CliConnectionStringProfileCatalog.Get("MySql");
        var password = Assert.Single(profile.Fields, field => field.Key.Equals("Password", StringComparison.OrdinalIgnoreCase));

        Assert.True(profile.SupportsWizard);
        Assert.True(password.Sensitive);
        Assert.Null(password.DefaultValue);

        var blankPassword = Assert.Single(
            CliConnectionStringProfileCatalog.Get("ClickHouse").Fields,
            field => field.Key.Equals("Password", StringComparison.OrdinalIgnoreCase));
        Assert.True(blankPassword.Sensitive);
        Assert.Null(blankPassword.DefaultValue);

        var fallback = CliConnectionStringProfileCatalog.Get("CustomDb");
        Assert.False(fallback.SupportsWizard);
        Assert.Equal("raw", fallback.Format);
        Assert.Empty(fallback.Fields);
    }

    [Fact]
    public void Build_ShouldRoundTripDelimiterCharactersWithoutChangingValues()
    {
        var result = _builder.Build("MySql", new Dictionary<string, string?>
        {
            ["Server"] = "localhost",
            ["Database"] = "app;Mode=debug",
            ["User"] = "root=admin",
            ["Password"] = "p;ass=word"
        });
        var parsed = new DbConnectionStringBuilder { ConnectionString = result };

        Assert.Equal("app;Mode=debug", parsed["Database"]);
        Assert.Equal("root=admin", parsed["User"]);
        Assert.Equal("p;ass=word", parsed["Password"]);
    }

    [Fact]
    public void BuildMongoDb_ShouldEncodeCredentials_AndAllowBlankPassword()
    {
        var result = _builder.Build("MongoDb", new Dictionary<string, string?>
        {
            ["Host"] = "localhost",
            ["Port"] = "27017",
            ["Database"] = "my db",
            ["Username"] = "user@example.com",
            ["Password"] = "p@ss:/word",
            ["AuthSource"] = "admin db"
        });

        Assert.StartsWith("mongodb://", result, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss:/word", result, StringComparison.Ordinal);
        Assert.Contains("authSource=admin%20db", result, StringComparison.Ordinal);

        var blankPassword = _builder.Build("MongoDb", new Dictionary<string, string?>
        {
            ["Host"] = "localhost",
            ["Username"] = "root",
            ["Password"] = ""
        });
        Assert.Contains("root@localhost", blankPassword, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldRejectUnknownFields_InvalidPorts_AndUnsupportedTypes()
    {
        Assert.Throws<InvalidOperationException>(() => _builder.Build("MySql", new Dictionary<string, string?>
        {
            ["Server"] = "localhost",
            ["Unknown"] = "value"
        }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build("MongoDb", new Dictionary<string, string?>
        {
            ["Host"] = "localhost",
            ["Port"] = "70000"
        }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build("CustomDb", new Dictionary<string, string?>()));
    }
}
