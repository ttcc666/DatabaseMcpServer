using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using System.Reflection;

namespace DatabaseMcpServer.Tests;

public class SqlSugarClientFactoryTests
{
    [Fact]
    public void ResetClientPool_ShouldRecreateClientWithUpdatedConfiguration()
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
        IDatabaseOptimizationStrategyFactory strategyFactory = new DatabaseOptimizationStrategyFactory();
        using var factory = new SqlSugarClientFactory(NullLogger<SqlSugarClientFactory>.Instance, helper, strategyFactory);

        var originalConnection = new DatabaseConnection
        {
            Name = "primary",
            ConnectionString = "Server=localhost;Database=main;User Id=sa;Password=secret;",
            DbType = "SqlServer",
            IsDefault = true
        };

        var updatedConnection = new DatabaseConnection
        {
            Name = "primary",
            ConnectionString = "Server=localhost;Database=main_v2;User Id=sa;Password=secret;",
            DbType = "SqlServer",
            IsDefault = true
        };

        var firstClient = factory.CreateClient(originalConnection);
        var cachedClient = factory.CreateClient(originalConnection);

        Assert.Same(firstClient, cachedClient);
        Assert.Equal(originalConnection.ConnectionString, firstClient.CurrentConnectionConfig.ConnectionString);

        factory.ResetClientPool();

        var retiredClients = GetRetiredClients(factory);
        Assert.Contains(firstClient, retiredClients);

        var refreshedClient = factory.CreateClient(updatedConnection);

        Assert.NotSame(firstClient, refreshedClient);
        Assert.Equal(updatedConnection.ConnectionString, refreshedClient.CurrentConnectionConfig.ConnectionString);

        factory.Dispose();
        Assert.Empty(retiredClients);
    }

    private static List<SqlSugarScope> GetRetiredClients(SqlSugarClientFactory factory)
    {
        var field = typeof(SqlSugarClientFactory).GetField("_retiredClients", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<List<SqlSugarScope>>(field?.GetValue(factory));
    }
}
