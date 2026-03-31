using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

internal sealed class SqlSugarClientFactory : ISqlSugarClientFactory
{
    private readonly ILogger<SqlSugarClientFactory> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly IDatabaseOptimizationStrategyFactory _strategyFactory;
    private readonly Dictionary<string, SqlSugarScope> _clientPool = new(StringComparer.Ordinal);
    private readonly object _poolLock = new();

    public SqlSugarClientFactory(
        ILogger<SqlSugarClientFactory> logger,
        IDatabaseHelperService databaseHelper,
        IDatabaseOptimizationStrategyFactory strategyFactory)
    {
        _logger = logger;
        _databaseHelper = databaseHelper;
        _strategyFactory = strategyFactory;
    }

    public ISqlSugarClient CreateClient(DatabaseConnection connection)
    {
        if (_clientPool.TryGetValue(connection.Name, out var existingScope))
        {
            _logger.LogDebug("复用连接池中的数据库客户端: {Name}", connection.Name);
            return existingScope;
        }

        lock (_poolLock)
        {
            if (_clientPool.TryGetValue(connection.Name, out existingScope))
            {
                return existingScope;
            }

            _logger.LogDebug("创建新的 SqlSugarScope 客户端: {Name}", connection.Name);

            var dbType = _databaseHelper.ParseDbType(connection.DbType);
            var scope = new SqlSugarScope(new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
                MoreSettings = CreateOptimizedSettings(dbType, connection)
            }, ConfigureAop);

            _clientPool[connection.Name] = scope;
            _logger.LogInformation("SqlSugarScope 客户端创建成功并加入连接池: {Name} ({DbType})", connection.Name, dbType);

            return scope;
        }
    }

    private ConnMoreSettings CreateOptimizedSettings(DbType dbType, DatabaseConnection connection)
    {
        var settings = new ConnMoreSettings
        {
            IsAutoRemoveDataCache = true,
            SqlServerCodeFirstNvarchar = true
        };

        try
        {
            var strategy = _strategyFactory.GetStrategy(dbType);
            strategy.ApplyOptimizations(settings, connection.OptimizationSettings);
            _logger.LogDebug("已为数据库 {Name} 应用性能优化: {Description}", connection.Name, strategy.GetDescription());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "应用数据库 {Name} 优化策略时出错，使用默认配置", connection.Name);
        }

        return settings;
    }

    private void ConfigureAop(SqlSugarClient db)
    {
        db.Aop.OnLogExecuting = (sql, pars) =>
        {
            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(sql);
            var safeParameters = SqlLogSanitizer.FormatParametersForLog(pars);
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", safeSql, safeParameters);
        };

        db.Aop.OnError = exp =>
        {
            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(exp.Sql);
            _logger.LogError("SQL执行错误: {Message} | SQL: {Sql}", exp.Message, safeSql);
        };
    }
}
