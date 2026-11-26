using DatabaseMcpServer.Models;
using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class ProviderIntegrationTests
{
    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task GlobalAdmission_QueuesAcrossConnectionsAndRecoversAfterDeadline(string provider)
    {
        using var fixture = new ProviderFixture(provider, configureServices: services =>
            services.AddSingleton(new DatabaseExecutionOptions { MaxConcurrent = 4, MaxTotalConcurrent = 1 }));
        var coordinator = fixture.Tool<DatabaseExecutionCoordinator>();
        using var other = coordinator.Reserve(new() { Name = "other", DbType = "Sqlite", ConnectionString = "Data Source=:memory:" }).Acquire();
        var waiting = fixture.StructuredQuery.QueryDatabase("SELECT 7 AS value", commandTimeoutSeconds: 1);
        Assert.False(waiting.IsCompleted);
        var timeout = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(timeout.IsError);
        Assert.Equal(1011, timeout.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
        other.Dispose();
        using var recovered = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase("SELECT 7 AS value"));
        Assert.Equal(7, recovered.RootElement.GetProperty("data")[0].GetProperty("value").GetInt32());
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task TinyNumbers_RoundTripWithoutBecomingZero(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        foreach (var maxRows in new int?[] { null, 1 })
        {
            using var result = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase(
                "SELECT @value AS tiny", parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":1e-50}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, maxRows: maxRows).Json());
            Assert.Equal(1e-50, result.RootElement.GetProperty("data")[0].GetProperty("tiny").GetDouble());
        }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task ParametersAndTypedRows_RoundTripThroughTools(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "dbmcp_" + Guid.NewGuid().ToString("N");
        var textType = provider == "SqlServer" ? "nvarchar(100)" : "varchar(100)";
        var dateType = provider == "PostgreSQL" ? "timestamp" : "datetime";
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table} (id bigint PRIMARY KEY, label {textType}, amount decimal(18,4), optional_value int NULL, happened {dateType})");
        try
        {
            const string label = "中文 ' quote 🐼";
            using var inserted = ProviderFixture.Success(await fixture.Command.ExecuteCommand(
                $"INSERT INTO {table} (id,label,amount,optional_value,happened) VALUES (@id,@label,@amount,@optional,'2026-11-20 12:34:56')",
                [new() { Name = "id", Value = JsonSerializer.SerializeToElement(9007199254740993L) },
                 new() { Name = "label", Value = JsonSerializer.SerializeToElement(label) },
                 new() { Name = "amount", Value = JsonSerializer.SerializeToElement(12345678901234.5678m) },
                 new() { Name = "optional", DbType = DatabaseParameterType.Int32 }]));
            Assert.Equal(1, inserted.RootElement.GetProperty("affectedRows").GetInt32());
            var sql = $"SELECT id,label,amount,optional_value,happened FROM {table} WHERE id=@id";
            using var unlimited = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase(sql, parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"id","value":9007199254740993}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!).Json());
            using var bounded = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase(sql, parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"id","value":9007199254740993}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, maxRows: 5, maxResponseBytes: 4096).Json());
            foreach (var result in new[] { unlimited, bounded })
            {
                Assert.Equal(1, result.RootElement.GetProperty("rowCount").GetInt32());
                var row = result.RootElement.GetProperty("data")[0];
                Assert.Equal(label, row.GetProperty("label").GetString());
                Assert.Equal(9007199254740993L, row.GetProperty("id").GetInt64());
                Assert.Equal(12345678901234.5678m, row.GetProperty("amount").GetDecimal());
                Assert.Equal(JsonValueKind.Null, row.GetProperty("optional_value").ValueKind);
                Assert.Equal(new DateTime(2026, 11, 20, 12, 34, 56), row.GetProperty("happened").GetDateTime());
            }
            using var inResult = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase(
                $"SELECT id FROM {table} WHERE id IN (@ids) AND label=@label", parameters: [new() { Name = "ids", Value = JsonSerializer.SerializeToElement(new long[] { 9007199254740993, 2 }) }, new() { Name = "label", Value = JsonSerializer.SerializeToElement(label) }]).Json());
            Assert.Equal(1, inResult.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(9007199254740993L, inResult.RootElement.GetProperty("data")[0].GetProperty("id").GetInt64());
            using var empty = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase(sql, parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"id","value":99}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
            Assert.Empty(empty.RootElement.GetProperty("data").EnumerateArray());
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task MultipleResults_PreserveEmptySetsTypesAndOrder(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        using var result = ProviderFixture.Success(await fixture.Query.GetDataSetAll(
            "SELECT CAST(@value AS decimal(18,4)) AS amount; SELECT 1 AS id WHERE 1=0; SELECT 9 AS id", JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":12.25}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        var root = result.RootElement;
        Assert.Equal(3, root.GetProperty("resultSetCount").GetInt32());
        var sets = root.GetProperty("resultSets");
        Assert.Equal(new[] { 1, 0, 1 }, sets.EnumerateArray().Select(set => set.GetProperty("rowCount").GetInt32()));
        Assert.Equal(12.25m, sets[0].GetProperty("data")[0].GetProperty("amount").GetDecimal());
        Assert.Empty(sets[1].GetProperty("data").EnumerateArray());
        Assert.Equal(9, sets[2].GetProperty("data")[0].GetProperty("id").GetInt32());
    }

    [ProviderTheory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("PostgreSQL", false)]
    [InlineData("PostgreSQL", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    public async Task InFlightCancellation_StopsServerWorkAndReleasesSlot(string provider, bool query)
    {
        using var fixture = new ProviderFixture(provider);
        using var cancellation = new CancellationTokenSource();
        var marker = "dbmcp_" + Guid.NewGuid().ToString("N");
        var request = query
            ? fixture.StructuredQuery.QueryDatabase(fixture.SlowQuery(marker), cancellationToken: cancellation.Token).Json()
            : fixture.Command.ExecuteCommand(fixture.SlowCommand(marker), cancellationToken: cancellation.Token);
        try
        {
            await fixture.WaitForActivity(marker, true);
            Assert.False(request.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(15)));
            await fixture.WaitForActivity(marker, false);
            await fixture.AssertUsable();
        }
        finally
        {
            cancellation.Cancel();
            try { await request.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { }
        }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task CommandTimeout_ReturnsFailureAndConnectionRemainsUsable(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var marker = "dbmcp_" + Guid.NewGuid().ToString("N");
        var table = "dbmcp_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table} (id int PRIMARY KEY, amount int)");
        try
        {
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {table} VALUES (1,0)");
            fixture.Observer.Ado.BeginTran();
            try
            {
                fixture.Observer.Ado.ExecuteCommand($"UPDATE {table} SET amount=1 WHERE id=1");
                var watch = Stopwatch.StartNew();
                using var result = JsonDocument.Parse(await fixture.Command.ExecuteCommand(
                    $"/*{marker}*/ UPDATE {table} SET amount=2 WHERE id=1", commandTimeoutSeconds: 1));
                Assert.False(result.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(1011, result.RootElement.GetProperty("errorCode").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(result.RootElement.GetProperty("errorMessage").GetString()));
                Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(15));
            }
            finally { fixture.Observer.Ado.RollbackTran(); }
            await fixture.WaitForActivity(marker, false);
            using var unchanged = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT amount AS value FROM {table} WHERE id=1"));
            Assert.Equal(0, unchanged.RootElement.GetProperty("data")[0].GetProperty("value").GetInt32());
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task BatchCancellation_KeepsCompletedWriteAndSkipsFollowingWrite(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "dbmcp_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table} (id int PRIMARY KEY)");
        using var cancellation = new CancellationTokenSource();
        var marker = "dbmcp_" + Guid.NewGuid().ToString("N");
        DatabaseCommand[] commands = [new() { Sql = $"INSERT INTO {table} VALUES (1)" }, new() { Sql = fixture.SlowCommand(marker) }, new() { Sql = $"INSERT INTO {table} VALUES (2)" }];
        var request = fixture.Command.BatchExecuteCommands(commands, cancellationToken: cancellation.Token);
        try
        {
            await fixture.WaitForActivity(marker, true);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(15)));
            await fixture.WaitForActivity(marker, false);
            using var rows = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT id FROM {table}").Json());
            Assert.Equal(1, rows.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(1, rows.RootElement.GetProperty("data")[0].GetProperty("id").GetInt32());
        }
        finally
        {
            cancellation.Cancel();
            try { await request.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
        }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task StoredProcedure_ReturnsRowsAndOutputThenAllowsTextQuery(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var procedure = "dbmcp_" + Guid.NewGuid().ToString("N");
        var sql = provider == "MySql"
            ? $"CREATE PROCEDURE {procedure}(IN input_value INT, OUT output_value INT) BEGIN SET output_value=input_value+1; SELECT input_value AS received; END"
            : $"CREATE PROCEDURE {procedure} @input_value INT, @output_value INT OUTPUT AS BEGIN SET NOCOUNT ON; SET @output_value=@input_value+1; SELECT @input_value AS received; END";
        fixture.Observer.Ado.ExecuteCommand(sql);
        try
        {
            using var result = ProviderFixture.Success(await fixture.Command.CallStoredProcedure(
                procedure, [new() { Name = "@input_value", DbType = DatabaseParameterType.Int32, Value = JsonSerializer.SerializeToElement(41) }, new() { Name = "@output_value", DbType = DatabaseParameterType.Int32, Direction = DatabaseParameterDirection.Output }]));
            Assert.Equal(1, result.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(41, result.RootElement.GetProperty("data")[0].GetProperty("received").GetInt32());
            var output = Assert.Single(result.RootElement.GetProperty("outputParameters").EnumerateObject());
            Assert.Equal("output_value", output.Name.TrimStart('@'));
            Assert.Equal(42, output.Value.GetInt32());
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP PROCEDURE {procedure}"); }
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    public async Task PostgreSqlFunction_IsInvokedWithParameterizedSelect(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var function = "dbmcp_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE FUNCTION {function}(input_value bigint) RETURNS bigint LANGUAGE SQL AS 'SELECT input_value+1'");
        try
        {
            using var result = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT {function}(@value) AS value", parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":41}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
            Assert.Equal(42, result.RootElement.GetProperty("data")[0].GetProperty("value").GetInt64());
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP FUNCTION {function}(bigint)"); }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public async Task SqlServerGo_ExecutesAllBatchesInOrder(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "dbmcp_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table} (id int PRIMARY KEY)");
        try
        {
            using var result = ProviderFixture.Success(fixture.Command.ExecuteCommandWithGo($"INSERT INTO {table} VALUES (1)\nGO\nINSERT INTO {table} VALUES (2)"));
            Assert.Equal(2, result.RootElement.GetProperty("affectedRows").GetInt32());
            using var count = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT COUNT(*) AS value FROM {table}"));
            Assert.Equal(2, count.RootElement.GetProperty("data")[0].GetProperty("value").GetInt32());
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }
}
