using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class InfrastructureAndSafetyTests
{
    [Theory]
    [InlineData("PRAGMA user_version=2026")]
    [InlineData("PRAGMA main.'user_version'(2026)")]
    [InlineData("PRAGMA [main].`journal_mode` = WAL")]
    [InlineData("PRAGMA foreign_keys(OFF)")]
    [InlineData("PRAGMA wal_checkpoint")]
    [InlineData("PRAGMA optimize")]
    [InlineData("PRAGMA incremental_vacuum")]
    [InlineData("EXPLAIN PRAGMA foreign_keys=OFF")]
    [InlineData("SELECT 1 INTO copy_table")]
    [InlineData("SELECT 1 INTO OUTFILE '/tmp/dbmcp-test'")]
    [InlineData("SELECT 1 /* ' */ INTO copy_table /* ' */")]
    [InlineData("SELECT 1 AS \"--\" INTO copy_table")]
    [InlineData("SELECT 1 AS \"'name\" INTO copy_table")]
    [InlineData("SELECT 1 /*! INTO OUTFILE '/tmp/dbmcp-test' */")]
    [InlineData("/*M! SET @value=1 */ SELECT 1")]
    public void EnsureReadOnlySql_RejectsSideEffects(string sql)
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
        var exception = Assert.Throws<DatabaseMcpException>(() => SqlSafetyGuard.EnsureReadOnlySql(sql, helper));
        Assert.Equal(DatabaseErrorCode.DangerousOperation, exception.ErrorCode);
    }

    [Theory]
    [InlineData("PRAGMA user_version;")]
    [InlineData("PRAGMA \"main\".\"user_version\"")]
    [InlineData("PRAGMA table_info('user''s table')")]
    [InlineData("PRAGMA main.table_xinfo = 'users'")]
    [InlineData("PRAGMA foreign_key_list(users)")]
    [InlineData("PRAGMA integrity_check(1)")]
    [InlineData("PRAGMA table_info('user;-- table') /* ; ' */")]
    [InlineData("/* leading */ PRAGMA /* inline */ main.user_version -- trailing")]
    [InlineData("SELECT 'INTO; PRAGMA user_version=5' AS value")]
    [InlineData("EXPLAIN SELECT 1")]
    public void EnsureReadOnlySql_AllowsMetadataAndReadOnlyStatements(string sql)
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
        SqlSafetyGuard.EnsureReadOnlySql(sql, helper);
    }

    [Fact]
    public void Binder_ShouldConvertCommonJsonValueKinds()
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);

        var parameters = SqlParameterBinder.Bind(JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"id","value":1},{"name":"name","value":"alice"},{"name":"enabled","value":true},{"name":"tags","value":[1,2]},{"name":"meta","value":{"key":"value"}}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, SqlSugar.DbType.Sqlite);

        Assert.NotNull(parameters);
        Assert.Equal(5, parameters.Length);
        Assert.Equal(1, Convert.ToInt32(parameters.Single(p => p.ParameterName == "id").Value));
        Assert.Equal("alice", parameters.Single(p => p.ParameterName == "name").Value);
        Assert.Equal(true, parameters.Single(p => p.ParameterName == "enabled").Value);
        Assert.IsType<object[]>(parameters.Single(p => p.ParameterName == "tags").Value);
        Assert.IsType<Dictionary<string, object?>>(parameters.Single(p => p.ParameterName == "meta").Value);
    }

    [Fact]
    public void EnsureReadOnlySql_ShouldRejectDangerousStatement()
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);

        var exception = Assert.Throws<DatabaseMcpException>(() => SqlSafetyGuard.EnsureReadOnlySql("DROP TABLE users", helper));

        Assert.Equal(DatabaseErrorCode.DangerousOperation, exception.ErrorCode);
    }

    [Theory]
    [InlineData("DELETE FROM users")]
    [InlineData("UPDATE users SET status = 'disabled'")]
    [InlineData("UPDATE users SET status = (SELECT status FROM defaults WHERE id = 1)")]
    [InlineData("UPDATE users SET note = 'WHERE id = 1'")]
    [InlineData("UPDATE users SET note = $$WHERE id = 1$$")]
    [InlineData("UPDATE users SET note = 'escaped\\' WHERE id = 1'")]
    [InlineData("UPDATE users SET status = 0 -- WHERE id = 1")]
    [InlineData("WITH active AS (SELECT id FROM users) UPDATE users SET status = 0")]
    [InlineData("WITH deleted AS (DELETE FROM users RETURNING id) SELECT * FROM deleted")]
    [InlineData("UPDATE users SET status = 1 WHERE id = 1\nGO\nDELETE FROM audit_logs")]
    public void DetectDangerousOperation_ShouldRejectMutationWithoutWhere(string sql)
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);

        Assert.True(helper.DetectDangerousOperation(sql));
    }

    [Theory]
    [InlineData("DELETE FROM users WHERE id = 1")]
    [InlineData("UPDATE users SET status = 'disabled' WHERE id = 1")]
    [InlineData("WITH active AS (SELECT id FROM users) UPDATE users SET status = 0 WHERE id IN (SELECT id FROM active)")]
    public void DetectDangerousOperation_ShouldAllowMutationWithWhere(string sql)
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);

        Assert.False(helper.DetectDangerousOperation(sql));
    }

    [Fact]
    public void McpExceptionFilter_ShouldSerializeStructuredErrorPayload()
    {
        var json = McpExceptionFilter.HandleException(
            new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "bad input"),
            NullLogger.Instance);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("bad input", document.RootElement.GetProperty("errorMessage").GetString());
        Assert.Equal((int)DatabaseErrorCode.InvalidParameters, document.RootElement.GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86_401)]
    public void SqlCommandTimeout_Validate_ShouldRejectOutOfRangeValues(int timeoutSeconds)
    {
        var exception = Assert.Throws<DatabaseMcpException>(() => SqlCommandTimeout.Validate(timeoutSeconds));

        Assert.Equal(DatabaseErrorCode.InvalidParameters, exception.ErrorCode);
        Assert.Contains("commandTimeoutSeconds", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(86_400)]
    public void SqlCommandTimeout_Validate_ShouldAcceptBoundaryValues(int timeoutSeconds)
    {
        SqlCommandTimeout.Validate(timeoutSeconds);
    }
}
