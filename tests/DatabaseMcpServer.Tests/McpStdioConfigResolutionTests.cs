using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

/// <summary>
/// 端到端测试 MCP stdio 模式启动时 <see cref="DatabaseConfigService"/> 的配置路径解析行为。
/// 这些测试覆盖：
/// - 未设置 DB_CONFIG_PATH 时自动回退到 %USERPROFILE%/.database-mcp/databases.json
/// - 设置 DB_CONFIG_PATH 时优先使用环境变量
/// - 整个回退链不应读取当前目录的 ./databases.json
/// </summary>
public class McpStdioConfigResolutionTests
{
    [Fact]
    public void DatabaseConfigService_ShouldStart_WhenEnvironmentUnset_AndUserProfileConfigExists()
    {
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userConfigPath = Path.Combine(userProfileDir, ".database-mcp", "databases.json");
        var fileExistedBefore = File.Exists(userConfigPath);
        var backupPath = userConfigPath + ".bak-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
            if (fileExistedBefore)
            {
                File.Move(userConfigPath, backupPath);
            }
            File.WriteAllText(userConfigPath, """
                {
                  "databases": [
                    { "name": "default", "connectionString": "Server=localhost", "dbType": "SqlServer", "isDefault": true }
                  ]
                }
                """);

            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", null);

            var service = CreateService();

            Assert.Equal(userConfigPath, service.GetConfigFilePath());
            Assert.Equal("default", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            if (File.Exists(userConfigPath))
            {
                File.Delete(userConfigPath);
            }
            if (fileExistedBefore)
            {
                File.Move(backupPath, userConfigPath);
            }
        }
    }

    [Fact]
    public void DatabaseConfigService_ShouldThrow_WhenEnvironmentPathIsMissing_AndNotFallBackToUserProfile()
    {
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var missingPath = Path.Combine(Path.GetTempPath(), $"dbmcp-missing-{Guid.NewGuid():N}.json");
        var userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userConfigPath = Path.Combine(userProfileDir, ".database-mcp", "databases.json");
        var fileExistedBefore = File.Exists(userConfigPath);
        var backupPath = userConfigPath + ".bak-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
            if (fileExistedBefore)
            {
                File.Move(userConfigPath, backupPath);
            }
            File.WriteAllText(userConfigPath, """
                { "databases": [ { "name": "user-profile", "connectionString": "Server=up", "dbType": "SqlServer", "isDefault": true } ] }
                """);

            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", missingPath);

            var exception = Assert.Throws<InvalidOperationException>(CreateService);
            Assert.Contains(Path.GetFullPath(missingPath), exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            if (File.Exists(userConfigPath))
            {
                File.Delete(userConfigPath);
            }
            if (fileExistedBefore)
            {
                File.Move(backupPath, userConfigPath);
            }
        }
    }

    [Fact]
    public void DatabaseConfigService_ShouldPreferEnvironment_OverUserProfile()
    {
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var explicitPath = Path.Combine(Path.GetTempPath(), $"dbmcp-explicit-{Guid.NewGuid():N}.json");
        var userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userConfigPath = Path.Combine(userProfileDir, ".database-mcp", "databases.json");
        var fileExistedBefore = File.Exists(userConfigPath);
        var backupPath = userConfigPath + ".bak-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
            if (fileExistedBefore)
            {
                File.Move(userConfigPath, backupPath);
            }
            File.WriteAllText(userConfigPath, """
                { "databases": [ { "name": "user-profile", "connectionString": "Server=up", "dbType": "SqlServer", "isDefault": true } ] }
                """);

            File.WriteAllText(explicitPath, """
                { "databases": [ { "name": "explicit", "connectionString": "Server=ex", "dbType": "SqlServer", "isDefault": true } ] }
                """);
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", explicitPath);

            var service = CreateService();

            Assert.Equal(explicitPath, service.GetConfigFilePath());
            Assert.Equal("explicit", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            File.Delete(explicitPath);
            if (File.Exists(userConfigPath))
            {
                File.Delete(userConfigPath);
            }
            if (fileExistedBefore)
            {
                File.Move(backupPath, userConfigPath);
            }
        }
    }

    private static DatabaseConfigService CreateService()
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
        var serializer = new JsonResultSerializer();
        var stateStore = new CurrentDatabaseStateStore(
            NullLogger<CurrentDatabaseStateStore>.Instance,
            enabled: false,
            stateFilePath: null);
        return new DatabaseConfigService(
            NullLogger<DatabaseConfigService>.Instance,
            helper,
            new NoopSqlSugarClientFactory(),
            serializer,
            stateStore);
    }

    private sealed class NoopSqlSugarClientFactory : ISqlSugarClientFactory
    {
        public ISqlSugarClient CreateClient(DatabaseConnection connection)
        {
            throw new NotSupportedException("McpStdioConfigResolutionTests 不需要创建数据库客户端。");
        }

        public void ResetClientPool()
        {
        }
    }
}
