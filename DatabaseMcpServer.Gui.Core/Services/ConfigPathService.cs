using System;
using System.IO;

namespace DatabaseMcpServer.Gui.Core.Services;

public enum PathSource
{
    EnvironmentVariable,
    UserProfile
}

public sealed record PathResolution(PathSource Source, string Path, string SourceLabel, bool ExistedAtStartup);

public sealed class ConfigPathService
{
    public const string EnvironmentVariableName = "DB_CONFIG_PATH";

    private const string FolderName = ".database-mcp";
    private const string FileName = "databases.json";

    public string UserProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        FolderName,
        FileName);

    public PathResolution TryResolveActive()
    {
        var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(environmentPath)
            ? CreateResolution(PathSource.UserProfile, UserProfilePath)
            : CreateResolution(PathSource.EnvironmentVariable, environmentPath);
    }

    public PathResolution CreateResolution(PathSource source, string? environmentPath = null)
    {
        var path = source == PathSource.UserProfile
            ? UserProfilePath
            : string.IsNullOrWhiteSpace(environmentPath)
                ? UserProfilePath
                : environmentPath;
        var normalizedPath = NormalizePath(path);
        var label = source == PathSource.EnvironmentVariable
            ? $"环境变量 {EnvironmentVariableName}"
            : "用户目录";

        return new PathResolution(source, normalizedPath, label, File.Exists(normalizedPath));
    }

    public string ResolveWritableDefault() => UserProfilePath;

    public string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("配置文件路径不能为空。");
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    public bool IsDefaultLocation(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        var normalized = NormalizePath(fullPath);
        var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.Equals(normalized, NormalizePath(UserProfilePath), StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(environmentPath)
                && string.Equals(normalized, NormalizePath(environmentPath), StringComparison.OrdinalIgnoreCase));
    }
}
