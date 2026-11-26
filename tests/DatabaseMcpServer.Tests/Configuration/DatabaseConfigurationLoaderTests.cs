using System.Security.Cryptography;
using System.Text;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class DatabaseConfigurationLoaderTests
{
    [Fact]
    public void Load_UsesOneTextForCandidateAndHash()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        const string content = """{"DATABASES":[{"name":"loaded","dbType":"Sqlite","connectionString":"Data Source=:memory:"}]}""";
        var reads = 0;
        var loader = new DatabaseConfigurationLoader(NullLogger<DatabaseConfigService>.Instance,
            new(false, fixture.Path), fixture.Directory.FullName, path =>
            {
                Assert.Equal(fixture.Path, path);
                reads++;
                return content;
            });

        Assert.True(loader.TryLoad(fixture.Path, out var snapshot, out var error));
        Assert.Empty(error);
        Assert.Equal(1, reads);
        Assert.Equal("loaded", Assert.Single(snapshot.Connections).Key);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), snapshot.ContentHash);
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void SavedCandidate_UsesEditorDocumentAndSavedTextHash()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        const string savedText = "saved file contents are hashed without parsing again";
        var candidate = new DatabasesConfig
        {
            EnableMonitorConfig = true,
            Databases = [new() { Name = "edited", DbType = "Sqlite", ConnectionString = "Data Source=:memory:", IsDefault = true }]
        };
        var reads = 0;
        var loader = new DatabaseConfigurationLoader(NullLogger<DatabaseConfigService>.Instance,
            new(false, fixture.Path), fixture.Directory.FullName, path =>
            {
                Assert.Equal(fixture.Path, path);
                reads++;
                return savedText;
            });

        var snapshot = loader.CreateSavedSnapshot(fixture.Path, candidate);
        Assert.Equal(1, reads);
        Assert.Equal("edited", Assert.Single(snapshot.Connections).Key);
        Assert.Equal("edited", snapshot.DefaultDatabaseName);
        Assert.True(snapshot.EnableMonitorConfig);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(savedText))), snapshot.ContentHash);
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
    }
}
