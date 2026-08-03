using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebApiService
{
    private readonly CliWebConfigContext _context;
    private readonly CliConfigFileService _configFileService;
    private readonly CliConfigCommandHandler _configCommandHandler;
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly CliConnectionStringBuilder _connectionStringBuilder;
    private readonly IServiceProvider _serviceProvider;

    public CliWebApiService(
        CliWebConfigContext context,
        CliConfigFileService configFileService,
        CliConfigCommandHandler configCommandHandler,
        ICurrentDatabaseStateStore currentDatabaseStateStore,
        CliConnectionStringBuilder connectionStringBuilder,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _configFileService = configFileService;
        _configCommandHandler = configCommandHandler;
        _currentDatabaseStateStore = currentDatabaseStateStore;
        _connectionStringBuilder = connectionStringBuilder;
        _serviceProvider = serviceProvider;
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
                currentDatabase = (string?)null,
                databases = Array.Empty<object>(),
                message = "配置文件不存在，可以先初始化。"
            };
        }

        try
        {
            var config = _configFileService.Load(_context.ConfigPath);
            var currentDefaultDatabase = CliConfigFileService.GetCurrentDefaultDatabaseName(config);
            var currentDatabase = ResolveCurrentDatabaseName(config, currentDefaultDatabase);

            return new
            {
                success = true,
                configPath = _context.ConfigPath,
                configSource = _context.Source,
                configExists = true,
                totalDatabases = config.Databases.Count,
                currentDefaultDatabase,
                currentDatabase,
                databases = config.Databases.Select(db => new
                {
                    name = db.Name,
                    dbType = db.DbType,
                    description = db.Description,
                    connectionString = ConnectionStringMasker.Mask(db.ConnectionString),
                    isDefault = db.IsDefault,
                    allowDangerousOperations = db.AllowDangerousOperations,
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
                currentDatabase = (string?)null,
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
                database = CliConfigFileService.ToMaskedConnection(database)
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
            totalPresets = CliConfigPresetCatalog.Presets.Count,
            presets = CliConfigPresetCatalog.Presets
                .OrderBy(item => item.DbType, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    dbType = item.DbType,
                    exampleName = item.ExampleName,
                    description = item.Description
                })
                .ToArray()
        };
    }

    public object GetPreset(string dbType)
    {
        if (!CliConfigPresetCatalog.TryGet(dbType, out var preset))
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
            profile = CliConnectionStringProfileCatalog.Get(dbType)
        };
    }

    public string Initialize(bool force)
    {
        var payload = _configCommandHandler.Initialize(_context.ConfigPath, force);
        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string CreateFromPreset(CliWebCreateFromPresetRequest request)
    {
        string? connectionString;
        try
        {
            connectionString = ResolveConnectionString(request.DbType, request.ConnectionString, request.ConnectionFields);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        var payload = _configCommandHandler.CreateFromPreset(
            _context.ConfigPath,
            request.DbType,
            request.Name,
            connectionString,
            request.Description,
            request.SetDefault,
            request.AllowDangerousOperations,
            request.PrintOnly);

        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string AddDatabase(CliWebAddDatabaseRequest request)
    {
        string? connectionString;
        try
        {
            connectionString = ResolveConnectionString(request.DbType, request.ConnectionString, request.ConnectionFields);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Failure("连接字符串不能为空。");
            }
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        var payload = _configCommandHandler.Add(
            _context.ConfigPath,
            request.Name,
            request.DbType,
            connectionString,
            request.Description,
            request.SetDefault,
            request.AllowDangerousOperations);

        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string RenameDatabase(string name, CliWebRenameDatabaseRequest request)
    {
        var currentDatabase = _currentDatabaseStateStore.GetCurrentDatabaseName(_context.ConfigPath);
        var payload = _configCommandHandler.Rename(_context.ConfigPath, name, request.NewName);

        if (IsSuccess(payload) && string.Equals(currentDatabase, name, StringComparison.Ordinal))
        {
            _currentDatabaseStateStore.SaveCurrentDatabaseName(_context.ConfigPath, request.NewName);
        }

        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string UpdateDatabase(string name, CliWebUpdateDatabaseRequest request)
    {
        string? connectionString;
        try
        {
            var dbType = request.DbType ?? ResolveDatabaseType(name);
            connectionString = ResolveConnectionString(dbType, request.ConnectionString, request.ConnectionFields);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        var payload = _configCommandHandler.Update(
            _context.ConfigPath,
            name,
            request.DbType,
            connectionString,
            request.Description,
            request.ClearDescription,
            request.SetDefault,
            request.ApplyDbType,
            request.ApplyConnectionString || request.ConnectionFields != null,
            request.ApplyDescription,
            request.ApplyClearDescription,
            request.ApplySetDefault,
            request.AllowDangerousOperations,
            request.ApplyAllowDangerousOperations);

        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string CloneDatabase(string name, CliWebCloneDatabaseRequest request)
    {
        var payload = _configCommandHandler.Clone(_context.ConfigPath, name, request.NewName, request.SetDefault);
        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string RemoveDatabase(string name)
    {
        var payload = _configCommandHandler.Remove(_context.ConfigPath, name);
        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string SetDefaultDatabase(string name)
    {
        var payload = _configCommandHandler.SetDefault(_context.ConfigPath, name);
        TryReloadRuntimeConfiguration(payload);
        return payload;
    }

    public string SwitchCurrentDatabase(string databaseName)
    {
        try
        {
            var tool = _serviceProvider.GetRequiredService<ConnectionTools>();
            return tool.SwitchDatabase(databaseName);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                currentDatabase = (string?)null,
                message = ex.Message
            });
        }
    }

    public string HealthCheck()
    {
        try
        {
            return _serviceProvider.GetRequiredService<ConnectionTools>().HealthCheck();
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public string Validate()
    {
        return _configCommandHandler.Validate(_context.ConfigPath);
    }

    public Task<string> DoctorAsync(CliWebDoctorRequest request)
    {
        return _configCommandHandler.DoctorAsync(
            _context.ConfigPath,
            request.Name,
            request.TestConnections,
            request.FixSuggestions,
            request.SummaryOnly);
    }

    public Task<string> TestConnectionAsync(string name)
    {
        return _configCommandHandler.TestAsync(_context.ConfigPath, name);
    }

    public async Task<string> ImportAsync(Stream stream, bool force, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"dbmcp-web-import-{Guid.NewGuid():N}.json");

        try
        {
            await using (var tempStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(tempStream, cancellationToken);
            }

            var payload = _configCommandHandler.Import(_context.ConfigPath, tempPath, force);
            TryReloadRuntimeConfiguration(payload);
            return payload;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    public (byte[] Contents, string DownloadName) Export()
    {
        if (!_context.ConfigExists)
        {
            throw new InvalidOperationException("配置文件不存在，无法导出。");
        }

        var fileName = Path.GetFileNameWithoutExtension(_context.ConfigPath);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return (File.ReadAllBytes(_context.ConfigPath), $"{fileName}-{timestamp}.json");
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

    private string ResolveDatabaseType(string name)
    {
        if (!_context.ConfigExists)
        {
            throw new InvalidOperationException("配置文件不存在，请先初始化。");
        }

        var config = _configFileService.Load(_context.ConfigPath);
        var database = config.Databases.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return database?.DbType ?? throw new InvalidOperationException($"数据库连接 '{name}' 不存在。");
    }

    private string? ResolveConnectionString(
        string dbType,
        string? connectionString,
        IReadOnlyDictionary<string, string?>? connectionFields)
    {
        if (connectionFields == null)
        {
            return connectionString;
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("原始连接字符串和向导字段不能同时提交。");
        }

        return _connectionStringBuilder.Build(dbType, connectionFields);
    }

    private void TryReloadRuntimeConfiguration(string payload)
    {
        if (!IsSuccess(payload))
        {
            return;
        }

        try
        {
            var databaseConfigService = _serviceProvider.GetService<IDatabaseConfigService>();
            _ = databaseConfigService?.ReloadConfiguration();
        }
        catch
        {
        }
    }

    private static bool IsSuccess(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("success", out var successElement) &&
                   successElement.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Failure(string message)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            message
        });
    }
}
