using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebConfigurationQueries
{
    private readonly CliWebConfigContext _context;
    private readonly ConfigurationFileStore _configFileService;
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly Func<IDatabaseConfigService?> _resolveRuntime;

    public CliWebConfigurationQueries(
        CliWebConfigContext context,
        ConfigurationFileStore configFileService,
        ICurrentDatabaseStateStore currentDatabaseStateStore,
        Func<IDatabaseConfigService?> resolveRuntime)
    {
        _context = context;
        _configFileService = configFileService;
        _currentDatabaseStateStore = currentDatabaseStateStore;
        _resolveRuntime = resolveRuntime;
    }

    public object GetContext()
    {
        return new
        {
            success = true,
            configPath = _context.ConfigPath,
            configSource = _context.Source,
            configExists = _context.ConfigExists
        };
    }

    public object GetDashboard()
    {
        DatabaseRuntimeStatus? runtime = null;
        string? runtimeError = null;
        try { runtime = _resolveRuntime()?.GetRuntimeStatus(); }
        catch (Exception ex) { runtimeError = ex.Message; }
        if (!_context.ConfigExists)
        {
            return new
            {
                success = true,
                configPath = _context.ConfigPath,
                configSource = _context.Source,
                configExists = false,
                totalDatabases = 0,
                currentDefaultDatabase = (string?)null,
                currentDatabase = runtime?.CurrentDatabase,
                runtime,
                runtimeError,
                databases = Array.Empty<object>(),
                message = "配置文件不存在，可以先初始化。"
            };
        }

        try
        {
            var config = _configFileService.Load(_context.ConfigPath);
            var currentDefaultDatabase = ConfigurationFileStore.GetCurrentDefaultDatabaseName(config);
            var currentDatabase = runtime?.CurrentDatabase;

            return new
            {
                success = true,
                configPath = _context.ConfigPath,
                configSource = _context.Source,
                configExists = true,
                totalDatabases = config.Databases.Count,
                currentDefaultDatabase,
                currentDatabase,
                runtime,
                runtimeError,
                persistedCurrentDatabase = ResolveCurrentDatabaseName(config, currentDefaultDatabase),
                databases = config.Databases.Select(db => new
                {
                    name = db.Name,
                    dbType = db.DbType,
                    description = db.Description,
                    connectionString = ConnectionStringMasker.Mask(db.ConnectionString),
                    isDefault = db.IsDefault,
                    enableDangerousOperations = db.EnableDangerousOperations,
                    isCurrent = string.Equals(db.Name, currentDatabase, StringComparison.Ordinal),
                    optimizationSettings = db.OptimizationSettings
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                configPath = _context.ConfigPath,
                configSource = _context.Source,
                configExists = true,
                totalDatabases = 0,
                currentDefaultDatabase = (string?)null,
                currentDatabase = runtime?.CurrentDatabase,
                runtime,
                runtimeError,
                databases = Array.Empty<object>(),
                message = ex.Message
            };
        }
    }

    public object GetDatabase(string name)
    {
        if (!_context.ConfigExists)
        {
            return new
            {
                success = false,
                configPath = _context.ConfigPath,
                message = "配置文件不存在，请先初始化。"
            };
        }

        try
        {
            var config = _configFileService.Load(_context.ConfigPath);
            var database = config.Databases.FirstOrDefault(db => string.Equals(db.Name, name, StringComparison.Ordinal));
            if (database == null)
            {
                return new
                {
                    success = false,
                    configPath = _context.ConfigPath,
                    databaseName = name,
                    message = $"数据库连接 '{name}' 不存在。"
                };
            }

            return new
            {
                success = true,
                configPath = _context.ConfigPath,
                database = ConfigurationFileStore.ToMaskedConnection(database)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                configPath = _context.ConfigPath,
                databaseName = name,
                message = ex.Message
            };
        }
    }

    public object GetPresets()
    {
        return new
        {
            success = true,
            totalPresets = DatabasePresetCatalog.Presets.Count,
            presets = DatabasePresetCatalog.Presets
                .OrderBy(item => item.DbType, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    dbType = item.DbType,
                    exampleName = item.ExampleName,
                    availability = item.Availability,
                    unavailableReason = item.UnavailableReason,
                    description = item.Description
                })
                .ToArray()
        };
    }

    public object GetPreset(string dbType)
    {
        if (!DatabasePresetCatalog.TryGet(dbType, out var preset))
        {
            return new
            {
                success = false,
                dbType,
                message = $"未找到数据库类型 '{dbType}' 的内置模板。"
            };
        }

        return new
        {
            success = true,
            preset = new
            {
                dbType = preset.DbType,
                exampleName = preset.ExampleName,
                availability = preset.Availability,
                unavailableReason = preset.UnavailableReason,
                exampleConnectionString = preset.ExampleConnectionString,
                description = preset.Description
            }
        };
    }

    public object GetConnectionStringProfile(string dbType)
    {
        return new
        {
            success = true,
            profile = ConnectionStringProfileCatalog.Get(dbType)
        };
    }

    private string? ResolveCurrentDatabaseName(DatabasesConfig config, string? currentDefaultDatabase)
    {
        var persistedCurrentDatabase = _currentDatabaseStateStore.GetCurrentDatabaseName(_context.ConfigPath);
        if (!string.IsNullOrWhiteSpace(persistedCurrentDatabase) &&
            config.Databases.Any(db => string.Equals(db.Name, persistedCurrentDatabase, StringComparison.Ordinal)))
        {
            return persistedCurrentDatabase;
        }

        return currentDefaultDatabase ?? config.Databases.FirstOrDefault()?.Name;
    }
}
