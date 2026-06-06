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
}
