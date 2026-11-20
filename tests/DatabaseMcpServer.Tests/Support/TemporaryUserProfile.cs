namespace DatabaseMcpServer.Tests;

internal sealed class TemporaryUserProfile : IDisposable
{
    public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("dbmcp-profile-").FullName;

    public void Dispose()
    {
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
