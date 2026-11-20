using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using System.Security.AccessControl;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class CliConfigWriteTests
{
    [Fact]
    public void Publish_ShouldKeepExistingReaderOnOldVersion()
    {
        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "databases.json");
        var files = new CliConfigFileService();
        Assert.True(files.Initialize(path, false));
        using var oldReader = ConfigFileReader.Open(path);

        var result = new CliConfigCommandHandler().Add(path, "new", "Sqlite", "Data Source=:memory:", null, true, false);

        using var response = JsonDocument.Parse(result);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), result);
        using var previous = JsonDocument.Parse(oldReader);
        Assert.Equal(0, previous.RootElement.GetProperty("databases").GetArrayLength());
        Assert.Equal("new", Assert.Single(files.Load(path).Databases).Name);
    }

    [Fact]
    public async Task Add_ShouldKeepEveryConcurrentEditAndOneDefault()
    {
        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "databases.json");
        var files = new CliConfigFileService();
        Assert.True(files.Initialize(path, false));
        var names = Enumerable.Range(1, 12).Select(index => $"connection-{index}").ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = names.Select(async name =>
        {
            await start.Task;
            var result = new CliConfigCommandHandler().Add(path, name, "Sqlite", "Data Source=:memory:", null, true, false);
            using var response = JsonDocument.Parse(result);
            Assert.True(response.RootElement.GetProperty("success").GetBoolean(), result);
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(writes);

        var connections = files.Load(path).Databases;
        Assert.Equal(names.Order(), connections.Select(connection => connection.Name).Order());
        Assert.Single(connections, connection => connection.IsDefault);
    }

    [Fact]
    public void FailedPublish_ShouldKeepOriginalBytesAndRemoveStagingFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "databases.json");
        var files = new CliConfigFileService();
        Assert.True(files.Initialize(path, false));
        var original = File.ReadAllBytes(path);
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = new CliConfigCommandHandler().Add(path, "new", "Sqlite", "Data Source=:memory:", null, false, false);
            using var response = JsonDocument.Parse(result);
            Assert.False(response.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(directory.DirectoryPath, "*.tmp"));
        }

        Assert.False(files.Initialize(path, true));
    }

    [Fact]
    public void Replace_ShouldPreserveRestrictedConfigPermissions()
    {
        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "databases.json");
        var files = new CliConfigFileService();
        Assert.True(files.Initialize(path, false));

        if (OperatingSystem.IsWindows())
        {
            var file = new FileInfo(path);
            var acl = file.GetAccessControl(AccessControlSections.Access);
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            file.SetAccessControl(acl);
            var expected = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Assert.False(files.Initialize(path, true));
            Assert.Equal(expected, file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        else
        {
            const UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, expected);
            Assert.False(files.Initialize(path, true));
            Assert.Equal(expected, File.GetUnixFileMode(path));
        }
    }
}
