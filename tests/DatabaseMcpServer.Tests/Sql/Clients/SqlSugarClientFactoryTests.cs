using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

public class SqlSugarClientFactoryTests
{
    [Fact]
    public void MongoAop_LogsExecutionAndErrorsWithoutDocumentValues()
    {
        var logger = new CaptureLogger();
        using var factory = new SqlSugarClientFactory(logger, new DatabaseOptimizationStrategyFactory(), new DatabaseExecutionOptions());
        using var lease = factory.AcquireClient(new DatabaseConnection
        { Name = "mongo", DbType = "MongoDb", ConnectionString = "mongodb://localhost:1/test" });
        const string command = "insertMany users [{\"password\":\"private-document-value\"}";
        Assert.ThrowsAny<Exception>(() => lease.Client.Ado.ExecuteCommand(command));

        Assert.Equal(new[] { LogLevel.Information, LogLevel.Error }, logger.Entries.Select(entry => entry.Level));
        Assert.All(logger.Entries, entry =>
        {
            Assert.Contains("MongoDB", entry.Message);
            Assert.DoesNotContain("private-", entry.Message);
            Assert.DoesNotContain("password", entry.Message);
            Assert.Null(entry.Exception);
        });
    }

    private sealed class CaptureLogger : ILogger<SqlSugarClientFactory>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((level, formatter(state, exception), exception));
    }

    [Fact]
    public void UpdateConnections_PreservesIdentityAndCapturesNewConfiguration()
    {
        using var factory = CreateFactory();
        var original = Connection("primary");
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [original.Name] = original });
        string identity;
        using (var first = factory.AcquireClient(original))
        {
            identity = Assert.IsType<string>(first.Context.ConnectionIdentity);
            Assert.False(first.Context.EnableDangerousOperations);
            Assert.True(first.Client.CurrentConnectionConfig.MoreSettings.SqliteCodeFirstEnableDefaultValue);
        }
        var updated = original.Copy();
        updated.EnableDangerousOperations = true;
        updated.ConnectionString = "Data Source=updated.db";
        updated.OptimizationSettings!["enableDefaultValue"] = "false";
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [updated.Name] = updated });
        using var next = factory.AcquireClient(updated);
        Assert.Equal(identity, next.Context.ConnectionIdentity);
        Assert.True(next.Context.EnableDangerousOperations);
        Assert.Equal(updated.ConnectionString, next.Client.CurrentConnectionConfig.ConnectionString);
        Assert.False(next.Client.CurrentConnectionConfig.MoreSettings.SqliteCodeFirstEnableDefaultValue);
        Assert.Equal("Data Source=:memory:", original.ConnectionString);
    }

    [Fact]
    public void RemovalAndReaddition_ReleasesIdentityAfterLastLease()
    {
        using var factory = CreateFactory();
        var connection = Connection("primary");
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [connection.Name] = connection });
        using var first = factory.AcquireClient(connection);
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection>());
        using (var concurrent = factory.AcquireClient(connection))
        {
            Assert.NotSame(first.Client, concurrent.Client);
            Assert.Equal(first.Context.ConnectionIdentity, concurrent.Context.ConnectionIdentity);
            Assert.Equal(1, first.Client.Ado.GetInt("select 1"));
        }
        first.Dispose();
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [connection.Name] = connection });
        using var replacement = factory.AcquireClient(connection);
        Assert.NotEqual(first.Context.ConnectionIdentity, replacement.Context.ConnectionIdentity);
    }

    private static SqlSugarClientFactory CreateFactory() => new(NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseOptimizationStrategyFactory(), new DatabaseExecutionOptions());
    private static DatabaseConnection Connection(string name) => new()
    {
        Name = name, DbType = "Sqlite", ConnectionString = "Data Source=:memory:",
        OptimizationSettings = new() { ["enableDefaultValue"] = "true" }
    };
}
