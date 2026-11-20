using DatabaseMcpServer.Models;
using System.Text.Json;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class QueryToolsTests
{
    [Theory]
    [InlineData("PRAGMA user_version=2026")]
    [InlineData("PRAGMA main.\"user_version\"(2026)")]
    public async Task ReadOnlyPragma_RejectsWritesWithoutChangingDatabase(string sql)
    {
        await InvokeQuery(async tool =>
        {
            using var before = JsonDocument.Parse(await tool.QueryDatabase("PRAGMA user_version").Json());
            Assert.Equal(0, before.RootElement.GetProperty("data")[0].GetProperty("user_version").GetInt32());
            using var rejected = JsonDocument.Parse(await tool.QueryDatabase(sql).Json());
            Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1003, rejected.RootElement.GetProperty("errorCode").GetInt32());
            using var metadata = JsonDocument.Parse(await tool.QueryDatabase("PRAGMA table_info('sqlite_master')").Json());
            Assert.Contains(metadata.RootElement.GetProperty("data").EnumerateArray(),
                column => column.GetProperty("name").GetString() == "rootpage");
            var after = await tool.QueryDatabase("PRAGMA user_version").Json();
            using var result = JsonDocument.Parse(after);
            Assert.Equal(0, result.RootElement.GetProperty("data")[0].GetProperty("user_version").GetInt32());
            return after;
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task QueryDatabase_PreservesTinyNumbersThroughSqlite(int? maxRows)
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase("select CAST(@value AS REAL) as value", parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":1e-50}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, maxRows: maxRows).Json());
        using var result = JsonDocument.Parse(payload);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(1e-50, result.RootElement.GetProperty("data")[0].GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task BoundedQuery_StopsBeforePoisonedNextRow()
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase(
            "select @value as Value union all select abs(-9223372036854775808)", parameters: JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":17}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!,
            commandTimeoutSeconds: 20, maxRows: 1).Json());
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(17, root.GetProperty("data")[0].GetProperty("Value").GetInt32());
        Assert.Equal(1, root.GetProperty("rowCount").GetInt32());
        Assert.True(root.GetProperty("limitReached").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("truncated").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BoundedQuery_DistinguishesEofFromRowLimit(int rowCount)
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase($"select 1 as v where {rowCount} > 0 union all select 2 where {rowCount} > 1", maxRows: 2).Json());
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(rowCount, root.GetProperty("rowCount").GetInt32());
        Assert.Equal(rowCount == 2, root.GetProperty("limitReached").GetBoolean());
        if (rowCount < 2) Assert.False(root.GetProperty("truncated").GetBoolean());
        else Assert.Equal(JsonValueKind.Null, root.GetProperty("truncated").ValueKind);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("binary")]
    [InlineData("escaped")]
    public async Task BoundedQuery_EnforcesUtf8BudgetAndReturnsWholeRows(string kind)
    {
        var sql = kind == "binary" ? "select zeroblob(1000000) as Value"
            : kind == "escaped" ? "select @value as Value"
            : "select 'first' as Value union all select hex(zeroblob(1000000))";
        var parameters = kind == "escaped" ? new DatabaseParameter[] { new() { Name = "value", Value = JsonSerializer.SerializeToElement(string.Concat(Enumerable.Repeat("\n值", 1000))) } } : null;
        var payload = await InvokeQuery(tool => tool.QueryDatabase(sql, parameters: parameters, maxResponseBytes: 1070).Json());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= 1070, payload);
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(document.RootElement.GetProperty("limitReached").GetBoolean());
        Assert.Equal(kind == "text" ? 1 : 0, document.RootElement.GetProperty("rowCount").GetInt32());
    }

    [Fact]
    public async Task BoundedInQuery_PreservesParameterBinding()
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase(
            "select @value as Value where @value in (@ids)", parameters: [new() { Name = "ids", Value = JsonSerializer.SerializeToElement(new[] { 2, 3 }) }, new() { Name = "value", Value = JsonSerializer.SerializeToElement("3") }], maxRows: 2).Json());
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal("3", document.RootElement.GetProperty("data")[0].GetProperty("Value").GetString());
    }

    [Fact]
    public async Task BoundedBatch_SharesRowsAndSkipsRemainingQueries()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""["select 1 as v", "select 2 as v union all select abs(-9223372036854775808)", "select * from missing_table"]""");
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!, maxRows: 2, maxResponseBytes: 4096);
        });
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(2, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(0, root.GetProperty("failedQueries").GetInt32());
        Assert.Equal(1, root.GetProperty("skippedQueries").GetInt32());
        Assert.False(root.GetProperty("results")[2].GetProperty("executed").GetBoolean());
        Assert.Equal(2, root.GetProperty("rowCount").GetInt32());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= 4096);
    }

    [Fact]
    public async Task BoundedBatch_SharesBytesAndStillReportsIndependentErrors()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""["drop table blocked", "select hex(zeroblob(1000000)) as value", "select * from missing_table"]""");
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!, maxResponseBytes: 2600);
        });
        using var response = JsonDocument.Parse(payload);
        var root = response.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(1, root.GetProperty("failedQueries").GetInt32());
        Assert.Equal(1, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(1, root.GetProperty("skippedQueries").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= 2600);
    }

    [Fact]
    public async Task BoundedBatch_DoesNotCountDiscardedRowsFromFailedItem()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""["select 1 union all select abs(-9223372036854775808)", "select 2 as v"]""");
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!, maxRows: 3);
        });
        using var result = JsonDocument.Parse(payload);
        Assert.Equal(1, result.RootElement.GetProperty("rowCount").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("failedQueries").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("results")[1].GetProperty("data")[0].GetProperty("v").GetInt32());
    }

    [Theory]
    [InlineData("unicode")]
    [InlineData("escaping")]
    public async Task BoundedQuery_CountsEncodedBytesInsteadOfCharacters(string kind)
    {
        var value = new string(kind == "unicode" ? '汉' : '\n', 250);
        var payload = await InvokeQuery(tool => tool.QueryDatabase("select @value as v", parameters: [new() { Name = "value", Value = JsonSerializer.SerializeToElement(value) }], maxResponseBytes: 1070).Json());
        using var response = JsonDocument.Parse(payload);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(0, response.RootElement.GetProperty("rowCount").GetInt32());
        Assert.True(response.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= 1070);
    }

    [Fact]
    public async Task BoundedMultiple_StopsBeforeNextStatementAndRejectsUnverifiedProvider()
    {
        var payload = await Invoke(tool => tool.GetDataSetAll("select 1 as v; select * from missing_table", maxRows: 1));
        using var result = JsonDocument.Parse(payload);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(1, result.RootElement.GetProperty("resultSetCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("truncated").ValueKind);
        using var unconnected = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
        { DbType = SqlSugar.DbType.Oracle, ConnectionString = "Data Source=127.0.0.1:1521/unused;User Id=unused;Password=unused" });
        var error = await Assert.ThrowsAsync<DatabaseMcpServer.Models.DatabaseMcpException>(() =>
            new DatabaseMcpServer.Helpers.BoundedQueryExecutor(1, null).MultipleAsync(unconnected, "select 1", []));
        Assert.Equal(DatabaseMcpServer.Models.DatabaseErrorCode.UnsupportedCapability, error.ErrorCode);
    }

    [Fact]
    public async Task BoundedMultiple_PreservesSQLiteMappingAndEmptySets()
    {
        const string sql = "select @value as ' Mixed ', 8 as ' Mixed ', x'00ff' as Binary, NULL as Missing; select 1 as Empty where 1=0; select '值' as Label union all select 'next'";
        var bounded = await Invoke(tool => tool.GetDataSetAll(sql, JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":7}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, maxRows: 10, maxResponseBytes: 4096));
        using var expected = JsonDocument.Parse("""[{"rowCount":1,"data":[{"Mixed":7,"Mixed1":8,"Binary":"AP8=","Missing":null}]},{"rowCount":0,"data":[]},{"rowCount":2,"data":[{"Label":"值"},{"Label":"next"}]}]""");
        using var actual = JsonDocument.Parse(bounded);
        Assert.True(actual.RootElement.GetProperty("success").GetBoolean(), bounded);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement.GetProperty("resultSets")), bounded);
        Assert.False(actual.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task BoundedMultiple_PreservesReaderValuesAndDuplicateColumnOrdinals()
    {
        var payload = await Invoke(async tool =>
        {
            const string sql = "select d,n,flag,1 as A,2 as b,3 as A from typed; select NULL as value union all select 7";
            var bounded = await tool.GetDataSetAll(sql, maxRows: 10);
            using var limited = JsonDocument.Parse(bounded);
            Assert.True(limited.RootElement.GetProperty("success").GetBoolean(), bounded);
            var sets = limited.RootElement.GetProperty("resultSets");
            var row = Assert.Single(sets[0].GetProperty("data").EnumerateArray());
            Assert.Equal("2026-11-19 12:34:56", row.GetProperty("d").GetString());
            Assert.Equal(12.25m, row.GetProperty("n").GetDecimal());
            Assert.Equal(1, row.GetProperty("flag").GetInt32());
            Assert.Equal(1, row.GetProperty("A").GetInt32());
            Assert.Equal(3, row.GetProperty("A5").GetInt32());
            Assert.Equal(JsonValueKind.Null, sets[1].GetProperty("data")[0].GetProperty("value").ValueKind);
            Assert.Equal(7, sets[1].GetProperty("data")[1].GetProperty("value").GetInt32());
            return bounded;
        }, prepare: db => db.Ado.ExecuteCommand("create table typed (d datetime, n decimal(18,2), flag boolean); insert into typed values ('2026-11-19 12:34:56', 12.25, 1)"));
        Assert.Contains("A5", payload);
    }

    [Fact]
    public async Task MultipleResults_KeepsMixedTypesAndResolvesAliasCollisions()
    {
        var payload = await Invoke(tool => tool.GetDataSetAll(
            "select NULL as value union all select 7 union all select 'text' union all select x'00ff'; select 1 as A, 2 as A, 3 as A1, 4 as a"));
        using var result = JsonDocument.Parse(payload);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), payload);
        var sets = result.RootElement.GetProperty("resultSets");
        var rows = sets[0].GetProperty("data");
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("value").ValueKind);
        Assert.Equal(7, rows[1].GetProperty("value").GetInt32());
        Assert.Equal("text", rows[2].GetProperty("value").GetString());
        Assert.Equal(new byte[] { 0, 255 }, rows[3].GetProperty("value").GetBytesFromBase64());
        var columns = Assert.Single(sets[1].GetProperty("data").EnumerateArray()).EnumerateObject().ToArray();
        Assert.Equal(4, columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(new[] { 1, 2, 3, 4 }, columns.Select(column => column.Value.GetInt32()));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(null, 255)]
    [InlineData(null, 0)]
    public async Task BoundedQuery_RejectsInvalidLimitsBeforeSqlExecution(int? rows, int? bytes)
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase("select * from missing_table", maxRows: rows, maxResponseBytes: bytes).Json());
        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("max", document.RootElement.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task GetDataSetAll_PreservesRowsNamesTypesAndEmptySets()
    {
        var payload = await Invoke(tool => tool.GetDataSetAll(
            "select @value as ' Mixed ', 8 as ' Mixed ', x'00ff' as Binary, NULL as Missing; select 1 as Empty where 1=0; select '值' as Label union all select 'next'",
            JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":7}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, commandTimeoutSeconds: 30));

        using var response = JsonDocument.Parse(payload);
        var root = response.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(3, root.GetProperty("resultSetCount").GetInt32());
        var sets = root.GetProperty("resultSets");
        Assert.Equal(new[] { 1, 0, 2 }, sets.EnumerateArray().Select(set => set.GetProperty("rowCount").GetInt32()));
        var row = Assert.Single(sets[0].GetProperty("data").EnumerateArray());
        Assert.Equal(7, row.GetProperty("Mixed").GetInt32());
        Assert.Equal(8, row.GetProperty("Mixed1").GetInt32());
        Assert.Equal("AP8=", row.GetProperty("Binary").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("Missing").ValueKind);
        Assert.Empty(sets[1].GetProperty("data").EnumerateArray());
        Assert.Equal(new[] { "值", "next" }, sets[2].GetProperty("data").EnumerateArray().Select(item => item.GetProperty("Label").GetString()));
    }

    [Fact]
    public async Task SqlQuerySingle_ShouldReadOnlyFirstRowAndPreserveColumnValues()
    {
        const string sql = "select @value as MixedCase, null as missing, X'0102' as bytes union all select abs(-9223372036854775808), null, null";
        var payload = await Invoke(tool => tool.SqlQuerySingle(sql, JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"value","value":17}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, commandTimeoutSeconds: 30));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        var row = root.GetProperty("data");
        Assert.Equal(17, row.GetProperty("MixedCase").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("missing").ValueKind);
        Assert.Equal(new byte[] { 1, 2 }, row.GetProperty("bytes").GetBytesFromBase64());
    }

    [Fact]
    public async Task SqlQuerySingle_ShouldReturnNullForEmptyResult()
    {
        var payload = await Invoke(tool => tool.SqlQuerySingle("select 1 where 1 = 0"));

        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task BatchQueryDatabase_ShouldExecuteFiveReadOnlyQueries()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "select 2", "select 3", "select 4", "select 5"]
                """);
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(5, root.GetProperty("totalQueries").GetInt32());
        Assert.Equal(5, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(0, root.GetProperty("failedQueries").GetInt32());
        Assert.Equal(5, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task BatchQueryDatabase_ShouldRejectMoreThanFiveQueries()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "select 2", "select 3", "select 4", "select 5", "select 6"]
                """);
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean(), payload);
        Assert.Contains("一次最多允许执行 5 条查询", root.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchQueryDatabase_ShouldReportUnsafeQueryAndContinue()
    {
        var payload = await Invoke(async tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "drop table users", "select 2"]
                """);
            return await tool.BatchSqlQuery(queries.RootElement.Deserialize<string[]>()!);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(3, root.GetProperty("totalQueries").GetInt32());
        Assert.Equal(2, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(1, root.GetProperty("failedQueries").GetInt32());

        var results = root.GetProperty("results");
        Assert.True(results[0].GetProperty("success").GetBoolean());
        Assert.False(results[1].GetProperty("success").GetBoolean());
        Assert.Equal(1, results[1].GetProperty("queryIndex").GetInt32());
        Assert.True(results[2].GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task QueryDatabase_ShouldRejectInvalidCommandTimeout()
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase("select 1", commandTimeoutSeconds: -1).Json());

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean(), payload);
        Assert.Contains("commandTimeoutSeconds", root.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryDatabase_ShouldAcceptExplicitCommandTimeout()
    {
        var payload = await InvokeQuery(tool => tool.QueryDatabase("select 1 as value", commandTimeoutSeconds: 60).Json());

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(1, root.GetProperty("rowCount").GetInt32());
    }

    private static Task<string> Invoke(Func<QueryTools, Task<string>> action, Action<SqlSugar.ISqlSugarClient>? prepare = null) => InvokeCore(action, prepare);
    private static Task<string> InvokeQuery(Func<DatabaseQueryTools, Task<string>> action) => InvokeCore(action, null);

    private static async Task<string> InvokeCore<T>(Func<T, Task<string>> action, Action<SqlSugar.ISqlSugarClient>? prepare) where T : notnull
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbmcp-query-{Guid.NewGuid():N}.db");
        var configPath = WriteSqliteConfigFile(databasePath);
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices();
            builder.Services.AddDatabaseMcpToolServices();

            using var host = builder.Build();
            if (prepare != null)
            {
                using var lease = host.Services.GetRequiredService<DatabaseMcpServer.Interfaces.IDatabaseConfigService>().AcquireClient();
                prepare(lease.Client);
            }
            return await action(host.Services.GetRequiredService<T>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
        }
    }

    private static string WriteSqliteConfigFile(string databasePath)
    {
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-query-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, $$"""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source={{escapedDatabasePath}};Cache=Shared;Mode=ReadWriteCreate;",
                  "dbType": "Sqlite",
                  "isDefault": true
                }
              ]
            }
            """);
        return configPath;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
