using System.Text.Json;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Web;

internal sealed class CliWebApiService
{
    private readonly CliWebConfigContext _context;
    private readonly ConfigurationCommands _configCommandHandler;
    private readonly ConnectionStringBuilder _connectionStringBuilder;
    private readonly CliWebConfigurationQueries _queries;
    private readonly ConfigurationRuntimeApplier _runtimeApplier;
    private readonly Func<IDatabaseConfigService> _resolveRuntime;
    private readonly Func<ConnectionOperations> _resolveConnectionOperations;

    public CliWebApiService(
        CliWebConfigContext context,
        ConfigurationCommands configCommandHandler,
        ConnectionStringBuilder connectionStringBuilder,
        CliWebConfigurationQueries queries,
        ConfigurationRuntimeApplier runtimeApplier,
        Func<IDatabaseConfigService> resolveRuntime,
        Func<ConnectionOperations> resolveConnectionOperations)
    {
        _context = context;
        _configCommandHandler = configCommandHandler;
        _connectionStringBuilder = connectionStringBuilder;
        _queries = queries;
        _runtimeApplier = runtimeApplier;
        _resolveRuntime = resolveRuntime;
        _resolveConnectionOperations = resolveConnectionOperations;
    }

    public object GetContext() => _queries.GetContext();

    public object GetDashboard() => _queries.GetDashboard();

    public object GetDatabase(string name) => _queries.GetDatabase(name);

    public object GetPresets() => _queries.GetPresets();

    public object GetPreset(string dbType) => _queries.GetPreset(dbType);

    public object GetConnectionStringProfile(string dbType) => _queries.GetConnectionStringProfile(dbType);

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
            var result = _resolveRuntime().RenameDatabase(name, request.NewName);
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
            var result = _resolveRuntime().UpdateDatabase(name, new DatabaseConnectionUpdate
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
            var tool = _resolveConnectionOperations();
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
            return Encode(await _resolveConnectionOperations().HealthCheck(cancellationToken));
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

            return ApplySavedConfiguration(_configCommandHandler.Import(_context.ConfigPath, tempPath, force));
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
                    // Preserve the import result if an external process temporarily locks the file.
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
        => Encode(_runtimeApplier
            .Apply(_context.ConfigPath, result, allowEmptyInitialization));

    private static string Encode(object result)
        => JsonSerializer.Serialize(result, JsonSerializationDefaults.CompactCamelCase);

    private static string Failure(string message) => Encode(new ConfigurationCommandResult { Success = false, Message = message });
}
