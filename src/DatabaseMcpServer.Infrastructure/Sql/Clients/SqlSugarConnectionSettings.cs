using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Services;

/// <summary>Builds the same ORM settings for execution and offline capability reporting.</summary>
internal sealed class SqlSugarConnectionSettings(IDatabaseOptimizationStrategyFactory strategies)
{
    public ConnectionConfig Create(DatabaseConnection connection)
    {
        var provider = DatabaseProviderCatalog.Parse(connection.DbType);
        DatabaseProviderCatalog.EnsureAvailable(provider);
        if (DatabaseProviderCatalog.ConnectionStringError(provider, connection.ConnectionString) is { } invalid)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, invalid);
        var errors = DatabaseOptimizationOptions.Errors(provider, connection.OptimizationSettings).ToArray();
        if (errors.Length > 0)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, string.Join("\n", errors));
        var settings = new ConnMoreSettings
        {
            IsAutoRemoveDataCache = true,
            SqlServerCodeFirstNvarchar = true
        };
        strategies.GetStrategy(provider).ApplyOptimizations(settings,
            DatabaseOptimizationOptions.AppliedOptions(provider, connection.OptimizationSettings));
        return new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString,
            DbType = provider,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = settings
        };
    }
}
