using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Cli;

internal sealed class CliConfigFileService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ResolveWritablePath(string? explicitConfigPath)
    {
        var resolution = CliConfigurationPathResolver.ResolveWritablePath(explicitConfigPath);
        if (!resolution.Success)
        {
            throw new InvalidOperationException(resolution.ErrorMessage);
        }

        return resolution.Path!;
    }

    public DatabasesConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"配置文件不存在: {path}。请先执行 'DatabaseMcpServer init' 或显式传入 '--config'。");
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<DatabasesConfig>(json, ReadOptions);
            if (config == null)
            {
                throw new InvalidOperationException($"配置文件内容为空或无法解析: {path}");
            }

            config.Databases ??= [];
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"配置文件 JSON 格式错误: {path}。{ex.Message}", ex);
        }
    }

    public void Save(string path, DatabasesConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonSerializationDefaults.IndentedCamelCase);
        File.WriteAllText(path, json);
    }

    public bool Initialize(string path, bool force)
    {
        var exists = File.Exists(path);
        if (exists && !force)
        {
            return false;
        }

        Save(path, new DatabasesConfig());
        return !exists;
    }

    public static string? GetCurrentDefaultDatabaseName(DatabasesConfig config)
    {
        return config.Databases.FirstOrDefault(db => db.IsDefault)?.Name;
    }

    public static object ToMaskedConnection(DatabaseConnection connection)
    {
        return new
        {
            name = connection.Name,
            connectionString = ConnectionStringMasker.Mask(connection.ConnectionString),
            dbType = connection.DbType,
            description = connection.Description,
            isDefault = connection.IsDefault,
            optimizationSettings = connection.OptimizationSettings
        };
    }
}
