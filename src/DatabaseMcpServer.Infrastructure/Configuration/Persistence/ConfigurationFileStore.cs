using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Configuration;

internal sealed class ConfigurationFileStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ResolveWritablePath(string? explicitConfigPath)
    {
        var resolution = ConfigurationPathResolver.ResolveWritablePath(explicitConfigPath);
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
            var json = ConfigFileReader.ReadAllText(path);
            var config = JsonSerializer.Deserialize<DatabasesConfig>(json, ReadOptions);
            if (config == null)
            {
                throw new InvalidOperationException($"配置文件内容为空或无法解析: {path}");
            }

            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"配置文件 JSON 格式错误: {path}。{ex.Message}", ex);
        }
    }

    public bool Initialize(string path, bool force)
    {
        using var write = new AtomicJsonFileWrite(path);
        var exists = write.Exists;
        if (exists && !force)
        {
            return false;
        }

        write.Save(new DatabasesConfig());
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
            enableDangerousOperations = connection.EnableDangerousOperations,
            optimizationSettings = connection.OptimizationSettings
        };
    }
}
