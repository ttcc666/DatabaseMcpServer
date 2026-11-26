using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using static DatabaseMcpServer.Configuration.ConfigurationCommandSupport;

namespace DatabaseMcpServer.Configuration;

/// <summary>Runs saved-configuration diagnostics and owns only resources created for each call.</summary>
internal sealed class ConfigurationDiagnosticCommands
{
    private readonly ConfigurationFileStore _configFileService;
    private readonly ISqlSugarClientFactory? _clientFactory;
    private readonly DatabaseConnectionProbe? _connectionProbe;
    private readonly DatabaseExecutionCoordinator? _execution;

    public ConfigurationDiagnosticCommands(ConfigurationFileStore configFileService,
        ISqlSugarClientFactory? clientFactory, DatabaseConnectionProbe? connectionProbe,
        DatabaseExecutionCoordinator? execution)
    {
        _configFileService = configFileService;
        _clientFactory = clientFactory;
        _connectionProbe = connectionProbe;
        _execution = execution;
    }

    private static SqlSugarClientFactory CreateDiagnosticFactory() => new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseMcpServer.Strategies.DBSetting.DatabaseOptimizationStrategyFactory(), DatabaseExecutionOptions.FromEnvironment());

    public async Task<ConfigurationCommandResult> DoctorAsync(string? explicitConfigPath, string? name, bool testConnections, bool includeFixSuggestions, bool summaryOnly,
        CancellationToken cancellationToken = default)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var report = DatabaseConfigurationValidator.Validate(config, requireConnections: testConnections);
            if (report.Errors.Count == 0 && !string.IsNullOrWhiteSpace(name))
            {
                var selected = FindConnection(config, name);
                config = new DatabasesConfig
                {
                    Databases = [selected]
                };
            }
            var fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, null)
                : Array.Empty<string>();

            if (!testConnections || report.Errors.Count > 0)
            {
                return new ConfigurationCommandResult
                {
                    Success = report.Errors.Count == 0,
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["databaseName"] = name,
                        ["totalDatabases"] = config.Databases?.Count ?? 0,
                        ["defaultDatabaseCount"] = report.DefaultDatabaseCount,
                        ["testedConnections"] = 0,
                        ["skippedConnectionTests"] = !testConnections || report.Errors.Count > 0,
                        ["skippedReason"] = !testConnections
                        ? "已显式关闭连通性测试。"
                        : "配置校验未通过，已跳过连通性测试。",
                        ["configErrors"] = summaryOnly ? null : report.Errors,
                        ["configWarnings"] = report.Warnings,
                        ["fixSuggestions"] = summaryOnly ? null : fixSuggestions,
                        ["connectionResults"] = summaryOnly ? null : Array.Empty<object>()
                    }
                };
            }

            var connectionResults = await CheckConnectionsAsync(config.Databases, cancellationToken);
            var failedConnections = connectionResults.Count(result => !result.Connected);
            fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, connectionResults)
                : Array.Empty<string>();

            return new ConfigurationCommandResult
            {
                Success = report.Errors.Count == 0 && failedConnections == 0,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = name,
                    ["totalDatabases"] = config.Databases.Count,
                    ["defaultDatabaseCount"] = report.DefaultDatabaseCount,
                    ["testedConnections"] = connectionResults.Count,
                    ["failedConnections"] = failedConnections,
                    ["skippedConnectionTests"] = false,
                    ["skippedReason"] = string.Empty,
                    ["configErrors"] = summaryOnly ? null : report.Errors,
                    ["configWarnings"] = report.Warnings,
                    ["fixSuggestions"] = summaryOnly ? null : fixSuggestions,
                    ["connectionResults"] = summaryOnly ? null : connectionResults
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message,
                Details = new()
                {
                    ["configPath"] = configPath
                }
            };
        }
    }

    public async Task<ConfigurationCommandResult> TestAsync(string? explicitConfigPath, string name, CancellationToken cancellationToken = default)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var connection = FindConnection(_configFileService.Load(configPath), name);
            var results = await CheckConnectionsAsync([connection], cancellationToken);
            var result = results[0];
            return new ConfigurationCommandResult
            {
                Success = result.Connected,
                Message = result.Message,
                Details = new()
                {
                    ["connected"] = result.Connected,
                    ["databaseName"] = name
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = name
                }
            };
        }
    }

    private async Task<List<DoctorConnectionResult>> CheckConnectionsAsync(
        IReadOnlyList<DatabaseConnection> connections, CancellationToken cancellationToken)
    {
        using var ownedFactory = _clientFactory == null ? CreateDiagnosticFactory() : null;
        using var ownedExecution = _execution == null ? new DatabaseExecutionCoordinator(DatabaseExecutionOptions.FromEnvironment()) : null;
        var execution = _execution ?? ownedExecution!;
        var probe = _connectionProbe ?? new DatabaseConnectionProbe(DatabaseExecutionOptions.FromEnvironment(), _clientFactory ?? ownedFactory!);
        var results = new List<DoctorConnectionResult>(connections.Count);
        foreach (var connection in connections)
        {
            var result = await DatabaseConnectionDiagnostics.CheckAsync(execution, connection, probe, cancellationToken);
            results.Add(new DoctorConnectionResult(connection.Name, connection.DbType, result.Connected, result.Message));
        }
        return results;
    }

    private static string[] BuildFixSuggestions(IReadOnlyCollection<string> configErrors, IReadOnlyCollection<DoctorConnectionResult>? connectionResults)
    {
        var suggestions = new List<string>();

        if (configErrors.Any(error => error.Contains("重复", StringComparison.Ordinal)))
        {
            suggestions.Add("先执行 'config rename' 或删除重复项，保证每个 name 唯一。");
        }

        if (configErrors.Any(error => error.Contains("缺少 name", StringComparison.Ordinal)))
        {
            suggestions.Add("补齐缺失的 name，或删除无效连接项。");
        }

        if (configErrors.Any(error => error.Contains("缺少 dbType", StringComparison.Ordinal)))
        {
            suggestions.Add("使用 'config update --db-type <type>' 补齐数据库类型。");
        }

        if (configErrors.Any(error => error.Contains("缺少 connectionString", StringComparison.Ordinal)))
        {
            suggestions.Add("使用 'config update --connection-string <value>' 补齐连接字符串。");
        }

        if (configErrors.Any(error => error.Contains("默认数据库连接数量超过 1", StringComparison.Ordinal)))
        {
            suggestions.Add("执行 'config use --name <name>' 或 'config set-default --name <name>' 只保留一个默认连接。");
        }

        if (connectionResults != null && connectionResults.Any(result => !result.Connected))
        {
            suggestions.Add("对失败连接先执行 'config show --name <name>' 检查连接串，再单独执行 'config test --name <name>' 复测。");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("未发现明显配置问题；若仍有异常，请单独运行 'config test --name <name>' 诊断具体连接。");
        }

        return suggestions.Distinct(StringComparer.Ordinal).ToArray();
    }
    private sealed record DoctorConnectionResult(
        string DatabaseName,
        string DatabaseType,
        bool Connected,
        string Message);
}
