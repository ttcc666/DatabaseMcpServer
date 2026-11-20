using System.Reflection;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void Assemblies_EnforceCoreAndInfrastructureDependencyDirection()
    {
        var core = typeof(DatabaseOperationContext).Assembly;
        var infrastructure = typeof(SqlSugarClientFactory).Assembly;
        var host = typeof(ToolCatalog).Assembly;
        Assert.Equal("DatabaseMcpServer.Core", core.GetName().Name);
        Assert.Equal("DatabaseMcpServer.Infrastructure", infrastructure.GetName().Name);
        Assert.All(core.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));
        Assert.Contains(infrastructure.GetReferencedAssemblies(), reference => reference.Name == core.GetName().Name);
        Assert.DoesNotContain(infrastructure.GetReferencedAssemblies(), reference =>
            reference.Name == host.GetName().Name || reference.Name!.StartsWith("ModelContextProtocol", StringComparison.Ordinal));
        Assert.DoesNotContain(infrastructure.GetTypes(), type =>
            type.Namespace is "DatabaseMcpServer.Cli" or "DatabaseMcpServer.Web" or "DatabaseMcpServer.Tools");

        foreach (var type in new ToolCatalog().Tools.Select(tool => tool.ToolType).Distinct())
        {
            var dependencies = type.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(p => p.ParameterType)
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.FieldType));
            Assert.DoesNotContain(dependencies, dependency =>
                dependency.Namespace?.StartsWith("SqlSugar", StringComparison.Ordinal) == true ||
                dependency.Namespace?.StartsWith("MongoDB", StringComparison.Ordinal) == true ||
                dependency == typeof(ISqlSugarClientFactory) || dependency == typeof(SqlDatabaseSessions));
        }
    }

    [Fact]
    public void Reservation_CopiesSettingsAndHidesConnectionSecrets()
    {
        using var execution = new DatabaseExecutionCoordinator(new());
        var connection = new DatabaseConnection
        {
            Name = "snapshot",
            DbType = "MongoDb",
            ConnectionString = "mongodb://user:private-password@localhost/test",
            ConfigurationVersion = 7,
            AccessMode = "read-only",
            OptimizationSettings = new() { ["lowercaseTables"] = "true" }
        };
        using var reservation = execution.Reserve(connection);
        connection.OptimizationSettings["lowercaseTables"] = "false";
        connection.AccessMode = "schema-admin";
        connection.ConfigurationVersion = 8;
        using var lease = reservation.Acquire();
        Assert.Equal("true", lease.Context.OptimizationSettings!["lowercaseTables"]);
        Assert.Equal("read-only", lease.Context.AccessMode);
        Assert.Equal(7, lease.Context.ConfigurationVersion);
        Assert.DoesNotContain("private-password", lease.Context.ToString());
        Assert.DoesNotContain("private-password", JsonSerializer.Serialize(lease.Context));
        Assert.Throws<InvalidOperationException>(() => reservation.Acquire());
    }

    [Fact]
    public void Coordinator_RejectsInvalidAdmissionPolicyBeforeAcceptingRequests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseExecutionCoordinator(new(MaxConcurrent: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseExecutionCoordinator(new(MaxQueued: -1)));
    }

    [Fact]
    public void MongoAndOfflineCapabilities_NeverCreateSqlClients()
    {
        var sqlCalls = 0;
        using var fixture = MongoCollectionServiceTests.Fixture(configure: services =>
            ObservedClientFactory.Register(services, _ => { sqlCalls++; throw new InvalidOperationException("SQL client forbidden"); }));
        var tools = fixture.Host.Services.GetRequiredService<ConnectionTools>();
        var mongo = DatabaseRoleTests.Success(tools.GetDatabaseCapabilities("primary"));
        Assert.Equal("MongoDb", mongo.GetProperty("actualProvider").GetString());
        Assert.Equal("supported", mongo.GetProperty("capabilities").GetProperty("collectionQueries").GetString());
        var sql = DatabaseRoleTests.Success(tools.GetDatabaseCapabilities("analytics"));
        Assert.Equal("Sqlite", sql.GetProperty("actualProvider").GetString());
        Assert.Equal(0, sqlCalls);
    }

    [Fact]
    public async Task MongoExecution_ReachesNativeRegistryWithoutSqlFactory()
    {
        var nativeCalls = 0;
        var sqlFactoryResolutions = 0;
        using var registry = new MongoClientRegistry((_, _) =>
        {
            nativeCalls++;
            throw new InvalidOperationException("native registry reached");
        });
        using var fixture = MongoCollectionServiceTests.Fixture(configure: services =>
        {
            services.AddSingleton(registry);
            services.AddSingleton<ISqlSugarClientFactory>(_ =>
            {
                sqlFactoryResolutions++;
                throw new InvalidOperationException("SQL factory forbidden");
            });
        });
        var result = await fixture.Host.Services.GetRequiredService<MongoCollectionService>()
            .ExecuteAsync(new(MongoOperation.Count, "documents"), default);
        Assert.False(result.Success);
        Assert.Equal(1, nativeCalls);
        Assert.Equal(0, sqlFactoryResolutions);
    }

    [Fact]
    public void ConfigurationEditFailure_PreservesOmittedOptionalFieldsAndSavedStatus()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var before = File.ReadAllText(fixture.Path);
        var result = new DatabaseMcpServer.Configuration.ConfigurationCommands().Update(
            fixture.Path, "missing", dbType: null, connectionString: null, description: "updated",
            clearDescription: false, setDefault: false, hasDbType: false, hasConnectionString: false,
            hasDescription: true, hasClearDescription: false, hasSetDefault: false,
            enableDangerousOperations: false, hasEnableDangerousOperations: false);
        var json = JsonSerializer.SerializeToElement(result, JsonSerializationDefaults.CompactCamelCase);
        Assert.False(result.Success);
        Assert.False(json.GetProperty("saved").GetBoolean());
        Assert.False(json.GetProperty("runtimeApplied").GetBoolean());
        Assert.Equal("missing", json.GetProperty("databaseName").GetString());
        Assert.False(json.TryGetProperty("database", out _));
        Assert.Equal(before, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void ResultAdapter_MapsSerializationFailureToExistingErrorContract()
    {
        var json = McpResultAdapter.Text(new { value = double.NaN });
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal((int)DatabaseErrorCode.UnknownError, document.RootElement.GetProperty("errorCode").GetInt32());
    }
}
