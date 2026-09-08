using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class CliConfigurationPathResolverTests
{
    [Fact]
    public void Resolve_ShouldUseExplicitConfigPath_WhenProvided()
    {
        var tempDirectory = CreateTempDirectory();
        var explicitConfigPath = Path.Combine(tempDirectory, "explicit.json");
        File.WriteAllText(explicitConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.Resolve(explicitConfigPath, tempDirectory, null, null);

            Assert.True(result.Success);
            Assert.Equal(Path.GetFullPath(explicitConfigPath), result.Path);
            Assert.Equal("--config", result.Source);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Resolve_ShouldPreferProjectDatabasesJson_OverOtherSources()
    {
        var currentDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var envDirectory = CreateTempDirectory();

        var projectConfigPath = Path.Combine(currentDirectory, "databases.json");
        var localConfigPath = Path.Combine(currentDirectory, "local-databases.json");
        var envConfigPath = Path.Combine(envDirectory, "databases.json");
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");

        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(projectConfigPath, "{}");
        File.WriteAllText(localConfigPath, "{}");
        File.WriteAllText(envConfigPath, "{}");
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.Resolve(null, currentDirectory, envConfigPath, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(projectConfigPath, result.Path);
            Assert.Equal("current-directory/databases.json", result.Source);
        }
        finally
        {
            DeleteDirectory(currentDirectory);
            DeleteDirectory(userDirectory);
            DeleteDirectory(envDirectory);
        }
    }

    [Fact]
    public void Resolve_ShouldFallbackToEnvironmentBeforeUserProfile()
    {
        var currentDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var envDirectory = CreateTempDirectory();

        var envConfigPath = Path.Combine(envDirectory, "databases.json");
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");

        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(envConfigPath, "{}");
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.Resolve(null, currentDirectory, envConfigPath, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(envConfigPath, result.Path);
            Assert.Equal("DB_CONFIG_PATH", result.Source);
        }
        finally
        {
            DeleteDirectory(currentDirectory);
            DeleteDirectory(userDirectory);
            DeleteDirectory(envDirectory);
        }
    }

    [Fact]
    public void Resolve_ShouldFallbackToUserProfile_WhenOtherSourcesAreMissing()
    {
        var currentDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.Resolve(null, currentDirectory, null, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(userConfigPath, result.Path);
            Assert.Equal("user-profile/.database-mcp/databases.json", result.Source);
        }
        finally
        {
            DeleteDirectory(currentDirectory);
            DeleteDirectory(userDirectory);
        }
    }

    [Fact]
    public void ResolveWritablePath_ShouldReturnExplicitPath_WhenProvided()
    {
        var relativePath = Path.Combine("temp", "databases.json");
        var path = CliConfigurationPathResolver.ResolveWritablePath(relativePath, Path.Combine("Users", "Tester"));

        Assert.True(path.Success);
        Assert.EndsWith(relativePath, path.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("--config", path.Source);
    }

    [Fact]
    public void ResolveWritablePath_ShouldDefaultToUserProfileLocation()
    {
        var userDirectory = CreateTempDirectory();

        try
        {
            var result = CliConfigurationPathResolver.ResolveWritablePath(null, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(Path.Combine(userDirectory, ".database-mcp", "databases.json"), result.Path);
            Assert.Equal("user-profile/.database-mcp/databases.json", result.Source);
        }
        finally
        {
            DeleteDirectory(userDirectory);
        }
    }

    [Fact]
    public void ResolveForMcpStdio_ShouldUseEnvironment_WhenFileExists()
    {
        var envDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var envConfigPath = Path.Combine(envDirectory, "databases.json");
        File.WriteAllText(envConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.ResolveForMcpStdio(envConfigPath, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(envConfigPath, result.Path);
            Assert.Equal("DB_CONFIG_PATH", result.Source);
        }
        finally
        {
            DeleteDirectory(envDirectory);
            DeleteDirectory(userDirectory);
        }
    }

    [Fact]
    public void Resolve_ShouldNotFallbackToUserProfile_WhenEnvironmentPathIsMissing()
    {
        var currentDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var missingEnvPath = Path.Combine(CreateTempDirectory(), "missing-databases.json");
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.Resolve(null, currentDirectory, missingEnvPath, userDirectory);

            Assert.False(result.Success);
            Assert.Equal("DB_CONFIG_PATH", result.Source);
            Assert.Contains(Path.GetFullPath(missingEnvPath), result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(currentDirectory);
            DeleteDirectory(userDirectory);
            DeleteDirectory(Path.GetDirectoryName(missingEnvPath)!);
        }
    }

    [Fact]
    public void ResolveForMcpStdio_ShouldError_WhenEnvironmentPathIsMissing_EvenIfUserProfileExists()
    {
        var userDirectory = CreateTempDirectory();
        var missingEnvPath = Path.Combine(CreateTempDirectory(), "missing-databases.json");
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.ResolveForMcpStdio(missingEnvPath, userDirectory);

            Assert.False(result.Success);
            Assert.Null(result.Path);
            Assert.Equal("DB_CONFIG_PATH", result.Source);
            Assert.Contains(Path.GetFullPath(missingEnvPath), result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(userDirectory);
            DeleteDirectory(Path.GetDirectoryName(missingEnvPath)!);
        }
    }

    [Fact]
    public void ResolveForMcpStdio_ShouldFallbackToUserProfile_WhenEnvironmentUnset()
    {
        var userDirectory = CreateTempDirectory();
        var userConfigPath = Path.Combine(userDirectory, ".database-mcp", "databases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
        File.WriteAllText(userConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.ResolveForMcpStdio(null, userDirectory);

            Assert.True(result.Success);
            Assert.Equal(userConfigPath, result.Path);
            Assert.Equal("user-profile/.database-mcp/databases.json", result.Source);
        }
        finally
        {
            DeleteDirectory(userDirectory);
        }
    }

    [Fact]
    public void ResolveForMcpStdio_ShouldIgnoreCurrentDirectoryEvenWhenPresent()
    {
        var cwd = CreateTempDirectory();
        var projectConfigPath = Path.Combine(cwd, "databases.json");
        File.WriteAllText(projectConfigPath, "{}");

        try
        {
            var result = CliConfigurationPathResolver.ResolveForMcpStdio(null, null);

            Assert.False(result.Success);
            Assert.Null(result.Path);
        }
        finally
        {
            DeleteDirectory(cwd);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
