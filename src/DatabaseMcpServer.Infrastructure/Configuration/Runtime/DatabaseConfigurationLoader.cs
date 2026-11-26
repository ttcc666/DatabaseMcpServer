using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

/// <summary>Loads configuration candidates without publishing runtime state.</summary>
internal sealed class DatabaseConfigurationLoader(
    ILogger<DatabaseConfigService> logger,
    DatabaseRuntimeOptions runtimeOptions,
    string userProfileDirectory,
    Func<string, string> readConfig)
{
    public string GetConfiguredPath()
    {
        CheckDeprecatedEnvironmentVariables();

        if (runtimeOptions.ConfigPath is { } configPath)
        {
            return Path.GetFullPath(configPath);
        }

        var environmentConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var resolution = ConfigurationPathResolver.ResolveForMcpStdio(
            environmentConfigPath,
            userProfileDirectory);

        if (!resolution.Success)
        {
            if (string.Equals(resolution.Source, "DB_CONFIG_PATH", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(resolution.ErrorMessage))
            {
                throw new InvalidOperationException(resolution.ErrorMessage);
            }

            throw new InvalidOperationException(
                "未找到数据库配置文件。请设置 DB_CONFIG_PATH 环境变量指向 databases.json 配置文件，\n" +
                "或在用户目录下创建 %USERPROFILE%/.database-mcp/databases.json。\n\n" +
                "示例:\n" +
                "  DB_CONFIG_PATH=D:\\config\\databases.json\n\n" +
                "配置文件格式请参考: databases.json.example");
        }

        logger.LogInformation("使用配置文件路径: {Path} (来源: {Source})", resolution.Path, resolution.Source);
        return resolution.Path!;
    }

    public bool TryLoad(
        string configPath,
        out DatabaseConfigurationSnapshot snapshot,
        out string errorMessage)
    {
        if (!File.Exists(configPath))
        {
            logger.LogError("配置文件不存在: {Path}", configPath);
            snapshot = DatabaseConfigurationSnapshot.Empty;
            errorMessage = $"配置文件不存在: {configPath}";
            return false;
        }

        try
        {
            var json = readConfig(configPath);
            var config = JsonSerializer.Deserialize<DatabasesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var validation = DatabaseConfigurationValidator.Validate(config, requireConnections: true);
            foreach (var warning in validation.Warnings) logger.LogWarning("配置诊断: {Warning}", warning);
            if (validation.Errors.Count > 0)
            {
                logger.LogError("配置文件中未找到有效的数据库配置: {Path}", configPath);
                snapshot = DatabaseConfigurationSnapshot.Empty;
                errorMessage = string.Join("\n", validation.Errors);
                return false;
            }

            var connections = new Dictionary<string, DatabaseConnection>(StringComparer.Ordinal);
            string? defaultDatabaseName = null;
            foreach (var db in config!.Databases)
            {
                connections[db.Name] = db;
                if (db.IsDefault && defaultDatabaseName == null)
                {
                    defaultDatabaseName = db.Name;
                }
            }

            snapshot = new DatabaseConfigurationSnapshot(
                configPath,
                connections,
                defaultDatabaseName,
                config.EnableMonitorConfig,
                HashContent(json));
            logger.LogInformation("成功从配置文件加载 {Count} 个数据库连接: {Path}", config.Databases.Count, configPath);
            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "配置文件 JSON 格式错误: {Path}", configPath);
            snapshot = DatabaseConfigurationSnapshot.Empty;
            errorMessage = $"配置文件 JSON 格式错误: {configPath}";
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "无法加载配置文件: {Path}", configPath);
            snapshot = DatabaseConfigurationSnapshot.Empty;
            errorMessage = $"无法加载配置文件: {configPath}";
            return false;
        }
    }

    public string? ReadContentHash(string configPath)
        => File.Exists(configPath) ? HashContent(readConfig(configPath)) : null;

    // Called by the editor while it still holds the configuration file's write lock.
    public DatabaseConfigurationSnapshot CreateSavedSnapshot(string configPath, DatabasesConfig config)
        => new(configPath,
            config.Databases.ToDictionary(db => db.Name, StringComparer.Ordinal),
            config.Databases.FirstOrDefault(db => db.IsDefault)?.Name, config.EnableMonitorConfig,
            HashContent(readConfig(configPath)));

    private static string HashContent(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private void CheckDeprecatedEnvironmentVariables()
    {
        var deprecatedVars = new[]
        {
            "DB_CONNECTION_STRING",
            "DB_TYPE",
            "DB_DM_LOWERCASE_TABLES",
            "DB_KDBNDP_MODE",
            "DB_GAUSSDB_NATIVE_DRIVER",
            "DB_ORACLE_CAMEL_CASE",
            "DB_POSTGRES_AUTO_TO_LOWER",
            "DB_SQLITE_ENABLE_DEFAULT_VALUE",
            "DB_DISABLE_NVARCHAR",
            "DB_DDL_WHITELIST"
        };

        var foundVars = deprecatedVars
            .Where(v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)))
            .ToList();

        if (!foundVars.Any())
        {
            return;
        }

        var message = new System.Text.StringBuilder();
        message.AppendLine("检测到已废弃的环境变量配置（DatabaseMcpServer 2.0.0+）：");
        message.AppendLine();

        foreach (var variable in foundVars)
        {
            message.AppendLine($"  - {variable}");
        }

        message.AppendLine();
        message.AppendLine("从 2.0.0 版本开始，所有配置都必须在 databases.json 文件中设置。");
        message.AppendLine();
        message.AppendLine("迁移示例：");
        message.AppendLine("旧方式（环境变量）：");
        message.AppendLine("  DB_CONNECTION_STRING=Server=localhost;...");
        message.AppendLine("  DB_TYPE=MySql");
        message.AppendLine("  DB_DM_LOWERCASE_TABLES=true");
        message.AppendLine();
        message.AppendLine("新方式（databases.json）：");
        message.AppendLine("  {");
        message.AppendLine("    \"databases\": [{");
        message.AppendLine("      \"name\": \"default\",");
        message.AppendLine("      \"connectionString\": \"Server=localhost;...\",");
        message.AppendLine("      \"dbType\": \"MySql\",");
        message.AppendLine("      \"optimizationSettings\": {");
        message.AppendLine("        \"lowercaseTables\": \"true\"");
        message.AppendLine("      }");
        message.AppendLine("    }]");
        message.AppendLine("  }");
        message.AppendLine();
        message.AppendLine("详细迁移指南请参考 README.md 中的\"从 1.x 迁移到 2.0\"章节");

        throw new InvalidOperationException(message.ToString());
    }

}
