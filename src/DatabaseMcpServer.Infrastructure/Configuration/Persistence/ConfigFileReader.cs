namespace DatabaseMcpServer.Helpers;

internal static class ConfigFileReader
{
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }
}
