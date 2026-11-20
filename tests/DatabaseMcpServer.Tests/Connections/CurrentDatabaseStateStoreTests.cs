using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class CurrentDatabaseStateStoreTests
{
    [Fact]
    public async Task ConcurrentStores_PreserveSelectionsForEveryConfig()
    {
        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "cli-state.json");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configs = Enumerable.Range(0, 12).Select(index => Path.Combine(directory.DirectoryPath, $"{index}.json")).ToArray();
        var writes = configs.Select(async config =>
        {
            var writer = Open(path);
            await start.Task;
            writer.SaveCurrentDatabaseName(config, Path.GetFileNameWithoutExtension(config));
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(writes);

        var reader = Open(path);
        Assert.All(configs, config => Assert.Equal(Path.GetFileNameWithoutExtension(config), reader.GetCurrentDatabaseName(config)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"entries\":null}")]
    public void Save_PreservesUnreadableState(string original)
    {
        using var directory = new TemporaryUserProfile();
        var path = Path.Combine(directory.DirectoryPath, "cli-state.json");
        var config = Path.Combine(directory.DirectoryPath, "db.json");
        File.WriteAllText(path, original);
        var store = Open(path);

        Assert.Null(store.GetCurrentDatabaseName(config));
        store.SaveCurrentDatabaseName(config, "analytics");

        Assert.Equal(original, File.ReadAllText(path));
    }

    private static CurrentDatabaseStateStore Open(string path) => new(NullLogger<CurrentDatabaseStateStore>.Instance, true, path);
}
