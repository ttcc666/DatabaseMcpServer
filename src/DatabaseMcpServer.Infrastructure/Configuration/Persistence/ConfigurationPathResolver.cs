namespace DatabaseMcpServer.Configuration;

internal sealed record ConfigurationPathResolution(
    string? Path,
    string? Source,
    string? ErrorMessage)
{
    public bool Success => ErrorMessage == null && Path != null;
}

internal static class ConfigurationPathResolver
{
    public static ConfigurationPathResolution Resolve(string? explicitConfigPath)
    {
        var currentDirectory = Environment.CurrentDirectory;
        var environmentPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Resolve(explicitConfigPath, currentDirectory, environmentPath, userProfile);
    }

    public static ConfigurationPathResolution ResolveWritablePath(string? explicitConfigPath)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return ResolveWritablePath(explicitConfigPath, userProfile);
    }

    internal static ConfigurationPathResolution Resolve(
        string? explicitConfigPath,
        string currentDirectory,
        string? environmentConfigPath,
        string? userProfileDirectory,
        bool includeCurrentDirectory = true)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            var fullPath = Path.GetFullPath(explicitConfigPath);
            return File.Exists(fullPath)
                ? new ConfigurationPathResolution(fullPath, "--config", null)
                : new ConfigurationPathResolution(null, "--config", $"配置文件不存在: {fullPath}");
        }

        if (includeCurrentDirectory)
        {
            var currentDatabases = Path.Combine(currentDirectory, "databases.json");
            if (File.Exists(currentDatabases))
            {
                return new ConfigurationPathResolution(currentDatabases, "current-directory/databases.json", null);
            }

            var currentLocalDatabases = Path.Combine(currentDirectory, "local-databases.json");
            if (File.Exists(currentLocalDatabases))
            {
                return new ConfigurationPathResolution(currentLocalDatabases, "current-directory/local-databases.json", null);
            }
        }

        if (!string.IsNullOrWhiteSpace(environmentConfigPath))
        {
            var fullEnvironmentPath = Path.GetFullPath(environmentConfigPath);
            return File.Exists(fullEnvironmentPath)
                ? new ConfigurationPathResolution(fullEnvironmentPath, "DB_CONFIG_PATH", null)
                : new ConfigurationPathResolution(null, "DB_CONFIG_PATH", $"配置文件不存在: {fullEnvironmentPath}");
        }

        if (!string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            var userConfigPath = Path.Combine(userProfileDirectory, ".database-mcp", "databases.json");
            if (File.Exists(userConfigPath))
            {
                return new ConfigurationPathResolution(userConfigPath, "user-profile/.database-mcp/databases.json", null);
            }
        }

        return new ConfigurationPathResolution(
            null,
            null,
            "未找到数据库配置文件。CLI 查找顺序: --config -> ./databases.json -> ./local-databases.json -> DB_CONFIG_PATH -> %USERPROFILE%/.database-mcp/databases.json");
    }

    /// <summary>
    /// 为 MCP stdio 模式解析配置路径，跳过当前目录查找。
    /// MCP 客户端（Claude Desktop、VS Code 等）从自己的目录启动子进程，
    /// 自动读取 ./databases.json 可能命中无关文件，因此只检查 DB_CONFIG_PATH 和用户目录。
    /// </summary>
    internal static ConfigurationPathResolution ResolveForMcpStdio(
        string? environmentConfigPath,
        string? userProfileDirectory)
    {
        return Resolve(
            explicitConfigPath: null,
            currentDirectory: string.Empty,
            environmentConfigPath: environmentConfigPath,
            userProfileDirectory: userProfileDirectory,
            includeCurrentDirectory: false);
    }

    internal static ConfigurationPathResolution ResolveWritablePath(
        string? explicitConfigPath,
        string? userProfileDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            return new ConfigurationPathResolution(Path.GetFullPath(explicitConfigPath), "--config", null);
        }

        if (string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            return new ConfigurationPathResolution(
                null,
                null,
                "无法确定用户目录，且未提供 '--config'。");
        }

        var userConfigPath = Path.Combine(userProfileDirectory, ".database-mcp", "databases.json");
        return new ConfigurationPathResolution(userConfigPath, "user-profile/.database-mcp/databases.json", null);
    }
}
