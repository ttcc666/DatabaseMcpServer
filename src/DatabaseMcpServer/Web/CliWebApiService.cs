using DatabaseMcpServer.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebApiService
{
    private readonly CliWebConfigContext _context;
    private readonly ConfigurationFileStore _configFileService;
    private readonly ConfigurationCommands _configCommandHandler;
    private readonly ICurrentDatabaseStateStore _currentDatabaseStateStore;
    private readonly ConnectionStringBuilder _connectionStringBuilder;
    private readonly IServiceProvider _serviceProvider;

    public CliWebApiService(
        CliWebConfigContext context,
        ConfigurationFileStore configFileService,
        ConfigurationCommands configCommandHandler,
        ICurrentDatabaseStateStore currentDatabaseStateStore,
        ConnectionStringBuilder connectionStringBuilder,
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
        DatabaseRuntimeStatus? runtime = null;
        string? runtimeError = null;
        try { runtime = _serviceProvider.GetService<IDatabaseConfigService>()?.GetRuntimeStatus(); }
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

    public string Initialize(bool force)
    {
        var payload = _configCommandHandler.Initialize(_context.ConfigPath, force);
        return ApplySavedConfiguration(payload, allowEmptyInitialization: true);
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
            request.EnableDangerousOperations,
            request.PrintOnly);

        return request.PrintOnly ? Encode(payload) : ApplySavedConfiguration(payload);
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
            request.EnableDangerousOperations);

        return ApplySavedConfiguration(payload);
    }

    public string RenameDatabase(string name, CliWebRenameDatabaseRequest request)
    {
        try
        {
            var result = _serviceProvider.GetRequiredService<IDatabaseConfigService>().RenameDatabase(name, request.NewName);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public string UpdateDatabase(string name, CliWebUpdateDatabaseRequest request)
    {
        try
        {
            var result = _serviceProvider.GetRequiredService<IDatabaseConfigService>().UpdateDatabase(name, new DatabaseConnectionUpdate
            {
                NewName = request.NewName, DbType = request.DbType, ConnectionString = request.ConnectionString,
                Description = request.Description, ClearDescription = request.ClearDescription, SetDefault = request.SetDefault,
                EnableDangerousOperations = request.EnableDangerousOperations, ApplyDbType = request.ApplyDbType,
                ApplyConnectionString = request.ApplyConnectionString || request.ConnectionFields != null,
                ApplyDescription = request.ApplyDescription, ApplyClearDescription = request.ApplyClearDescription,
                ApplySetDefault = request.ApplySetDefault, ApplyEnableDangerousOperations = request.ApplyEnableDangerousOperations,
                BuildConnectionString = request.ConnectionFields == null ? null : dbType => ResolveConnectionString(dbType, request.ConnectionString, request.ConnectionFields)
            });
            return JsonSerializer.Serialize(result, JsonSerializationDefaults.IndentedCamelCase);
        }
        catch (Exception ex) { return Failure(ex.Message); }
    }

    public string CloneDatabase(string name, CliWebCloneDatabaseRequest request)
    {
        var payload = _configCommandHandler.Clone(_context.ConfigPath, name, request.NewName, request.SetDefault);
        return ApplySavedConfiguration(payload);
    }

    public string RemoveDatabase(string name)
    {
        var payload = _configCommandHandler.Remove(_context.ConfigPath, name);
        return ApplySavedConfiguration(payload);
    }

    public string SetDefaultDatabase(string name)
    {
        var payload = _configCommandHandler.SetDefault(_context.ConfigPath, name);
        return ApplySavedConfiguration(payload);
    }

    public string SwitchCurrentDatabase(string databaseName)
    {
        try
        {
            var tool = _serviceProvider.GetRequiredService<ConnectionOperations>();
            return Encode(tool.SwitchDatabase(databaseName));
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

    public async Task<string> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Encode(await _serviceProvider.GetRequiredService<ConnectionOperations>().HealthCheck(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public string Validate()
    {
        return Encode(_configCommandHandler.Validate(_context.ConfigPath));
    }

    public async Task<string> DoctorAsync(CliWebDoctorRequest request, CancellationToken cancellationToken = default)
    {
        return Encode(await _configCommandHandler.DoctorAsync(
            _context.ConfigPath,
            request.Name,
            request.TestConnections,
            request.FixSuggestions,
            request.SummaryOnly, cancellationToken));
    }

    public async Task<string> TestConnectionAsync(string name, CancellationToken cancellationToken = default)
    {
        return Encode(await _configCommandHandler.TestAsync(_context.ConfigPath, name, cancellationToken));
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
            return ApplySavedConfiguration(payload);
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
        using var stream = ConfigFileReader.Open(_context.ConfigPath);
        var contents = new byte[checked((int)stream.Length)];
        stream.ReadExactly(contents);
        return (contents, $"{fileName}-{timestamp}.json");
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

    private string ApplySavedConfiguration(ConfigurationCommandResult result, bool allowEmptyInitialization = false)
        => Encode(_serviceProvider.GetRequiredService<ConfigurationRuntimeApplier>()
            .Apply(_context.ConfigPath, result, allowEmptyInitialization));

    private static string Encode(object result)
        => JsonSerializer.Serialize(result, JsonSerializationDefaults.CompactCamelCase);

    private static string Failure(string message) => Encode(new ConfigurationCommandResult { Success = false, Message = message });
}
