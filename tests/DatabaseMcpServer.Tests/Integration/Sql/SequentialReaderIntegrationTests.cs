using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Schema;
using DatabaseMcpServer.Tools.Management;
using SqlSugar;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class SequentialReaderIntegrationTests
{
    [ProviderTheory]
    [InlineData(16)]
    [InlineData(128)]
    [InlineData(1024)]
    [InlineData(1025)]
    public async Task SqlServerDeclaredTextWidths_Preserve1024BoundaryAndStreamingFallback(int width)
    {
        using var fixture = new ProviderFixture("SqlServer");
        var text = new string('x', width - 4) + "😀汉\"";
        var expected = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["value"] = text }, QueryRowEncoder.Options);
        var parameters = SqlParameterBinder.Bind([new() { Name = "text", Value = JsonSerializer.SerializeToElement(text) }],
            fixture.Observer.CurrentConnectionConfig.DbType);
        foreach (var budget in new[] { expected.Length - 1, expected.Length, 8192 })
        {
            await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer,
                $"SELECT CAST(@text AS nvarchar({width})) AS value", parameters, default))
            {
                Assert.True(lease.Sequential);
                Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
                using var encoder = new QueryRowEncoder(lease.Reader);
                if (budget < expected.Length)
                {
                    var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(budget, default));
                    Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
                }
                else
                {
                    Assert.Equal(expected, (await encoder.ReadAsync(budget, default)).Utf8Json);
                    Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
                    lease.Complete();
                }
            }
            Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
            await fixture.AssertUsable();
        }
    }

    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerBoundedShortText_PreservesPaddingUnicodeAndLowBudgetFallback(bool sequential)
    {
        using var fixture = new ProviderFixture("SqlServer");
        const string unicode = "汉😀\"\\\n";
        var expected = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["unicode"] = unicode, ["ansi"] = "é", ["paddedUnicode"] = unicode.PadRight(16),
            ["paddedAnsi"] = "é".PadRight(16), ["empty"] = "", ["missing"] = null
        }, QueryRowEncoder.Options);
        const string sql = "SELECT CAST(@label AS nvarchar(128)) AS unicode,CAST('é' AS varchar(128)) AS ansi," +
            "CAST(@label AS nchar(16)) AS paddedUnicode,CAST('é' AS char(16)) AS paddedAnsi," +
            "CAST(N'' AS nvarchar(16)) AS empty,CAST(NULL AS nvarchar(128)) AS missing";
        var parameters = SqlParameterBinder.Bind([new() { Name = "label", Value = JsonSerializer.SerializeToElement(unicode) }],
            fixture.Observer.CurrentConnectionConfig.DbType);
        foreach (var budget in new[] { expected.Length - 1, expected.Length, expected.Length + 1, 4096 })
        {
            await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer, sql, parameters, default, sequential))
            {
                Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
                using var encoder = new QueryRowEncoder(lease.Reader);
                if (budget < expected.Length)
                {
                    var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(budget, default));
                    Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
                }
                else
                {
                    Assert.Equal(expected, (await encoder.ReadAsync(budget, default)).Utf8Json);
                    Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
                    lease.Complete();
                }
            }
            Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
            await fixture.AssertUsable();
        }
    }

    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerLegacyDateTypes_PreserveTextReaderFailure(bool sequential)
    {
        using var fixture = new ProviderFixture("SqlServer");
        using var client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.SqlServer, IsAutoCloseConnection = true,
            ConnectionString = fixture.Observer.CurrentConnectionConfig.ConnectionString + "Type System Version=SQL Server 2005;"
        });
        const string sql = "SELECT CAST('2026-11-23T08:15:01.1234567' AS datetime2) AS at";
        Exception? reference;
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(client, sql, [], default, sequential))
        {
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            Assert.Equal(typeof(string), lease.Reader.GetFieldType(0));
            reference = Record.Exception(() => ((System.Data.Common.DbDataReader)lease.Reader).GetTextReader(0));
            Assert.NotNull(reference);
        }
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(client, sql, [], default, sequential))
        {
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            using var encoder = new QueryRowEncoder(lease.Reader);
            var error = await Record.ExceptionAsync(() => encoder.ReadAsync(4096, default));
            Assert.NotNull(error);
            Assert.Equal(reference.GetType(), error.GetType());
            Assert.Equal(reference.Message, error.Message);
        }
        Assert.Equal(System.Data.ConnectionState.Closed, client.Ado.Connection.State);
        Assert.Equal(17, client.Ado.GetInt("SELECT 17"));
    }

    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerNullUdt_PreservesNullWithoutLoadingClrType(bool sequential)
    {
        using var fixture = new ProviderFixture("SqlServer");
        const string text = "bounded short field in a UDT result set";
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer,
            "SELECT CAST(NULL AS geography) AS shape,CAST(@label AS varchar(128)) AS label",
            [new SugarParameter("@label", text)], default, sequential))
        {
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            using var encoder = new QueryRowEncoder(lease.Reader);
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
                { ["shape"] = null, ["label"] = text }, QueryRowEncoder.Options),
                (await encoder.ReadAsync(4096, default)).Utf8Json);
            Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
            lease.Complete();
        }
        Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
        await fixture.AssertUsable();
    }

    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerVariantAndResultSets_PreserveChangingTypesAndNullFirstRow(bool sequential)
    {
        using var fixture = new ProviderFixture("SqlServer");
        const string sql = "SELECT v FROM (VALUES (0,CAST(NULL AS sql_variant)),(1,CAST(CAST(9007199254740993 AS bigint) AS sql_variant))," +
            "(2,CAST(N'汉😀' AS sql_variant)),(3,CAST(CAST(123456789.123456789 AS decimal(28,9)) AS sql_variant))," +
            "(4,CAST(0x0080ff AS sql_variant))) AS changing(n,v) ORDER BY n;" +
            "SELECT CAST(NULL AS nvarchar(32)) AS v UNION ALL SELECT N'next汉😀'; SELECT 7 AS v";
        object?[][] expected = [[null, 9007199254740993L, "汉😀", 123456789.123456789m, new byte[] { 0, 128, 255 }],
            [null, "next汉😀"], [7]];
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer, sql, [], default, sequential))
        {
            Assert.Equal(sequential, lease.Sequential);
            foreach (var (values, index) in expected.Select((values, index) => (values, index)))
            {
                if (index > 0) Assert.True(await QueryReader.NextResultAsync(lease.Reader, default));
                using var encoder = new QueryRowEncoder(lease.Reader);
                foreach (var value in values)
                {
                    Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
                    var bytes = (await encoder.ReadAsync(4096, default)).Utf8Json;
                    Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["v"] = value },
                        QueryRowEncoder.Options), bytes);
                }
                Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
            }
            Assert.False(await QueryReader.NextResultAsync(lease.Reader, default));
            lease.Complete();
        }
        Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
        await fixture.AssertUsable();
    }

    [ProviderTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServerTypedFields_MatchExactJsonBytesAndBoundaries(bool sequential)
    {
        using var fixture = new ProviderFixture("SqlServer");
        var at = new DateTime(2026, 11, 23, 8, 15, 1).AddTicks(1234567);
        var text = new string('x', 1023) + "😀汉\"\\\n";
        var expected = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["id"] = 9007199254740993L, ["amount"] = 123456789.123456789m,
            ["label"] = text, ["ansi"] = "é", ["bytes"] = new byte[] { 0, 128, 255 }, ["at"] = at,
            ["offset"] = new DateTimeOffset(at, TimeSpan.FromHours(8)),
            ["time"] = new TimeSpan(8, 15, 1).Add(TimeSpan.FromTicks(1234567)),
            ["day"] = new DateTime(2026, 11, 23), ["guid"] = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ["missing"] = null
        }, QueryRowEncoder.Options);
        const string sql = "SELECT CAST(9007199254740993 AS bigint) AS id,CAST(123456789.123456789 AS decimal(28,9)) AS amount," +
            "CAST(@label AS nvarchar(max)) AS label,CAST('é' AS varchar(8)) AS ansi,CAST(0x0080ff AS varbinary(max)) AS bytes," +
            "CAST('2026-11-23T08:15:01.1234567' AS datetime2) AS at,CAST('2026-11-23T08:15:01.1234567+08:00' AS datetimeoffset) AS offset," +
            "CAST('08:15:01.1234567' AS time) AS time,CAST('2026-11-23' AS date) AS day," +
            "CAST('00112233-4455-6677-8899-aabbccddeeff' AS uniqueidentifier) AS guid,CAST(NULL AS nvarchar(8)) AS missing";
        var parameters = SqlParameterBinder.Bind([new() { Name = "label", Value = JsonSerializer.SerializeToElement(text) }],
            fixture.Observer.CurrentConnectionConfig.DbType);
        foreach (var budget in new[] { expected.Length - 1, expected.Length, expected.Length + 1 })
        {
            await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer, sql, parameters, default, sequential))
            {
                Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
                using var encoder = new QueryRowEncoder(lease.Reader);
                if (budget < expected.Length)
                {
                    var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(budget, default));
                    Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
                }
                else
                {
                    Assert.Equal(expected, (await encoder.ReadAsync(budget, default)).Utf8Json);
                    Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
                    lease.Complete();
                }
            }
            Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
            await fixture.AssertUsable();
        }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public async Task SqlServerTextRead_CancelsPendingDriverReadAndReusesConnection(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var marker = "field_cancel_" + Guid.NewGuid().ToString("N");
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(fixture.Observer,
            $"/*{marker}*/ SELECT REPLICATE(CAST('x' AS varchar(max)),67108864) AS value", [], cancellation.Token))
        {
            Assert.True(await QueryReader.ReadAsync(lease.Reader, cancellation.Token));
            using var text = new PendingDriverTextReader(((System.Data.Common.DbDataReader)lease.Reader).GetTextReader(0));
            async Task<string> Read() => await BudgetedFieldReader.ReadTextAsync(text, 67108866, cancellation.Token);
            var read = Read();
            await text.Pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(read.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
        await fixture.WaitForActivity(marker, false);
        await fixture.AssertUsable();
    }

    private sealed class PendingDriverTextReader(TextReader inner) : TextReader
    {
        public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var read = inner.ReadAsync(buffer, cancellationToken);
            // Observe an actual outstanding driver read; no artificial delay or blocked wrapper.
            if (!read.IsCompleted) Pending.TrySetResult();
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public void SchemaQualifiedRemarksAndDefaults_DoNotTouchSameNamedDboTable(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var schema = "schema_" + Guid.NewGuid().ToString("N")[..12];
        var table = "table_" + Guid.NewGuid().ToString("N")[..12];
        fixture.Observer.Ado.ExecuteCommand($"CREATE SCHEMA {schema}");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {schema}.{table}(id int,value int DEFAULT 1); CREATE TABLE dbo.{table}(id int,value int DEFAULT 2)");
        try
        {
            var tools = fixture.Tool<SchemaTools>();
            DatabaseRoleTests.Success(tools.AddTableRemark(table, "dbo-table"));
            DatabaseRoleTests.Success(tools.AddColumnRemark(table, "value", "dbo-column"));
            Assert.False(DatabaseRoleTests.Success(tools.IsAnyTableRemark($"{schema}.{table}")).GetProperty("exists").GetBoolean());
            DatabaseRoleTests.Success(tools.AddTableRemark($"{schema}.{table}", "first-table"));
            DatabaseRoleTests.Success(tools.AddTableRemark($"{schema}.{table}", "second-table"));
            DatabaseRoleTests.Success(tools.AddColumnRemark($"{schema}.{table}", "value", "first-column"));
            DatabaseRoleTests.Success(tools.AddColumnRemark($"{schema}.{table}", "value", "second-column"));
            string Remark(string target, int minor) => fixture.Observer.Ado.GetString(
                "SELECT CONVERT(nvarchar(4000),value) FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(@target) AND minor_id=@minor AND name='MS_Description'",
                new SugarParameter("@target", target), new SugarParameter("@minor", minor));
            Assert.Equal("second-table", Remark($"{schema}.{table}", 0));
            Assert.Equal("second-column", Remark($"{schema}.{table}", 2));
            Assert.Equal("dbo-table", Remark($"dbo.{table}", 0));
            Assert.Equal("dbo-column", Remark($"dbo.{table}", 2));
            DatabaseRoleTests.Success(tools.AddDefaultValue($"{schema}.{table}", "value", "7"));
            Assert.ThrowsAny<Exception>(() => SqlServerSchemaDialect.SetDefaultValue(fixture.Observer, $"{schema}.{table}", "value", "invalid_column_reference"));
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {schema}.{table}(id) VALUES(1); INSERT INTO dbo.{table}(id) VALUES(1)");
            Assert.Equal(7, fixture.Observer.Ado.GetInt($"SELECT value FROM {schema}.{table}"));
            Assert.Equal(2, fixture.Observer.Ado.GetInt($"SELECT value FROM dbo.{table}"));
            DatabaseRoleTests.Success(tools.DeleteColumnRemark($"{schema}.{table}", "value"));
            DatabaseRoleTests.Success(tools.DeleteTableRemark($"{schema}.{table}"));
            Assert.False(DatabaseRoleTests.Success(tools.IsAnyTableRemark($"{schema}.{table}")).GetProperty("exists").GetBoolean());
            Assert.True(DatabaseRoleTests.Success(tools.IsAnyTableRemark(table)).GetProperty("exists").GetBoolean());
            Assert.Equal("dbo-column", Remark($"dbo.{table}", 2));
            Assert.Equal(0, fixture.Observer.Ado.GetInt($"SELECT COUNT(*) FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID('{schema}.{table}')"));
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {schema}.{table}; DROP TABLE dbo.{table}; DROP SCHEMA {schema}"); }
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task SequentialReader_PreservesArrayParametersHooksAndForwardOnlyValues(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var db = fixture.Observer;
        var started = 0;
        var completed = 0;
        var errors = 0;
        db.Aop.OnLogExecuting = (_, _) => started++;
        db.Aop.OnLogExecuted = (_, _) => completed++;
        db.Aop.OnError = _ => errors++;
        var binary = provider == "PostgreSQL" ? "decode('0080ff','hex')" : "0x0080ff";
        var sql = $"SELECT 9007199254740993 AS id,@text AS label,{binary} AS bytes,CAST(NULL AS int) AS missing WHERE 2 IN (@ids)";
        var parameters = SqlParameterBinder.Bind([
            new() { Name = "text", Value = JsonSerializer.SerializeToElement(new string('x', 1023) + "😀汉\"\\\n") },
            new() { Name = "ids", Value = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }) }
        ], db.CurrentConnectionConfig.DbType);
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(db, sql, parameters, default))
        {
            Assert.True(lease.Sequential);
            Assert.Null(db.CurrentConnectionConfig.SqlMiddle);
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            using var encoder = new QueryRowEncoder(lease.Reader);
            var row = await encoder.ReadAsync(8192, default);
            using var json = JsonDocument.Parse(row.Utf8Json);
            Assert.Equal(9007199254740993L, json.RootElement.GetProperty("id").GetInt64());
            Assert.Equal(new string('x', 1023) + "😀汉\"\\\n", json.RootElement.GetProperty("label").GetString());
            Assert.Equal(new byte[] { 0, 128, 255 }, json.RootElement.GetProperty("bytes").GetBytesFromBase64());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("missing").ValueKind);
            Assert.False(await QueryReader.ReadAsync(lease.Reader, default));
            lease.Complete();
        }
        Assert.Equal(1, started);
        Assert.Equal(1, completed);
        Assert.Equal(0, errors);
        await Assert.ThrowsAnyAsync<Exception>(() => SqlSugarReaderAdapter.OpenAsync(db, "SELECT * FROM dbmcp_missing_table", [], default));
        Assert.Equal(1, errors);
        Assert.Null(db.CurrentConnectionConfig.SqlMiddle);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Ado.Connection.State);
        Assert.Equal(1, db.Ado.GetInt("SELECT 1"));
        db.Aop.OnLogExecuted = (_, _) => throw new InvalidOperationException("test-after-execute");
        var hookFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => SqlSugarReaderAdapter.OpenAsync(db, "SELECT 1 AS value", [], default));
        Assert.Equal("test-after-execute", hookFailure.Message);
        Assert.Null(db.CurrentConnectionConfig.SqlMiddle);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Ado.Connection.State);
        db.Aop.OnLogExecuted = null;
        Assert.Equal(5, db.Ado.GetInt("SELECT 5"));
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task SequentialReader_PreservesTransformTransactionAndCustomMiddleware(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var db = fixture.Observer;
        db.Aop.OnExecutingChangeSql = (sql, parameters) => new(sql.Replace("41", "42", StringComparison.Ordinal), parameters);
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(db, "SELECT 41 AS value", [], default))
        {
            Assert.True(lease.Sequential);
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            Assert.Equal(42, lease.Reader.GetInt32(0));
            lease.Complete();
        }
        db.Aop.OnExecutingChangeSql = null;
        db.Ado.BeginTran();
        var transaction = db.Ado.Transaction;
        try
        {
            await using var lease = await SqlSugarReaderAdapter.OpenAsync(db, "SELECT 7 AS value", [], default);
            Assert.False(lease.Sequential);
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            Assert.Equal(7, lease.Reader.GetInt32(0));
            lease.Complete();
        }
        finally { db.Ado.RollbackTran(); }
        Assert.NotNull(transaction);
        Assert.Equal(8, db.Ado.GetInt("SELECT 8"));

        var calls = 0;
        var middleware = new SqlMiddle { IsSqlMiddle = true, GetDataReaderAsync = (_, _) =>
        {
            calls++;
            var table = new System.Data.DataTable();
            table.Columns.Add("value", typeof(int));
            table.Rows.Add(99);
            return Task.FromResult<System.Data.IDataReader>(table.CreateDataReader());
        } };
        db.CurrentConnectionConfig.SqlMiddle = middleware;
        try
        {
            await using var lease = await SqlSugarReaderAdapter.OpenAsync(db, "SELECT 1", [], default);
            Assert.False(lease.Sequential);
            Assert.True(await QueryReader.ReadAsync(lease.Reader, default));
            Assert.Equal(99, lease.Reader.GetInt32(0));
            Assert.Same(middleware, db.CurrentConnectionConfig.SqlMiddle);
            Assert.Equal(1, calls);
            lease.Complete();
        }
        finally { db.CurrentConnectionConfig.SqlMiddle = null; }
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task SequentialExportAndBatch_PreserveValuesAndReleaseSingleAdmissionSlot(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var exporter = fixture.Tool<DatabaseMcpServer.Services.SqlExportService>();
        var binary = provider == "PostgreSQL" ? "decode('0080ff','hex')" : "0x0080ff";
        var text = "汉😀,\"x\"";
        foreach (var format in new[] { "csv", "jsonl" })
        {
            using var destination = new MemoryStream();
            var result = JsonSerializer.SerializeToElement(await exporter.Export(destination, $"SELECT @label AS label,{binary} AS bytes",
                [new() { Name = "label", Value = JsonSerializer.SerializeToElement(text) }], format, null, 1, 1, null, default),
                JsonSerializationDefaults.CompactCamelCase);
            Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
            Assert.Equal(1, result.GetProperty("rowCount").GetInt32());
            Assert.True(result.GetProperty("limitReached").GetBoolean());
            var exported = System.Text.Encoding.UTF8.GetString(destination.ToArray());
            if (format == "csv") Assert.Equal("\"label\",\"bytes\"\n\"汉😀,\"\"x\"\"\",\"AID/\"\n", exported);
            else
            {
                using var row = JsonDocument.Parse(exported);
                Assert.Equal(text, row.RootElement.GetProperty("label").GetString());
                Assert.Equal(new byte[] { 0, 128, 255 }, row.RootElement.GetProperty("bytes").GetBytesFromBase64());
            }
            await fixture.AssertUsable();
        }
        var batch = await new BoundedQueryExecutor(10, 8192).BatchAsync(fixture.Observer,
            [new() { Sql = "SELECT 7 AS value" }, new() { Sql = "SELECT 8 AS value" }], _ => { });
        using var json = JsonDocument.Parse(batch.Utf8Json);
        Assert.Equal(2, json.RootElement.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(new[] { 7, 8 }, json.RootElement.GetProperty("results").EnumerateArray()
            .Select(item => item.GetProperty("data")[0].GetProperty("value").GetInt32()));
        Assert.Equal(System.Data.ConnectionState.Closed, fixture.Observer.Ado.Connection.State);
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task SequentialReader_EarlyBudgetCancellationAndMultipleResultsReleaseConnection(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var db = fixture.Observer;
        var context = new DatabaseClientContext(db, false, "reader-test");
        var text = provider == "PostgreSQL" ? "repeat('x',8388608)" : "REPLICATE(CAST('x' AS varchar(max)),8388608)";
        var large = await new BoundedQueryExecutor(10, 4096).QueryResultAsync(context, $"SELECT {text} AS value", [], default);
        Assert.True(large.Truncated);
        Assert.Empty(large.Data);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Ado.Connection.State);
        Assert.Equal(3, db.Ado.GetInt("SELECT 3"));
        using var cancellation = new CancellationTokenSource();
        await using (var lease = await SqlSugarReaderAdapter.OpenAsync(db, $"SELECT {text} AS value", [], cancellation.Token))
        {
            Assert.True(await QueryReader.ReadAsync(lease.Reader, cancellation.Token));
            using var encoder = new QueryRowEncoder(lease.Reader);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => encoder.ReadAsync(4096, cancellation.Token));
        }
        Assert.Equal(4, db.Ado.GetInt("SELECT 4"));
        var result = await new BoundedQueryExecutor(10, 8192).MultipleAsync(db, "SELECT 1 AS a; SELECT 2 AS b", []);
        using var json = JsonDocument.Parse(result.Utf8Json);
        Assert.Equal(2, json.RootElement.GetProperty("resultSetCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("resultSets")[1].GetProperty("data")[0].GetProperty("b").GetInt32());
    }
}
