using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class InfrastructureAndSafetyTests
{
    [Fact]
    public void ParseParameters_ShouldConvertCommonJsonValueKinds()
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);

        var parameters = helper.ParseParameters("""{"id":1,"name":"alice","enabled":true,"tags":[1,2],"meta":{"key":"value"}}""");

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
