using System.Text.Json;
using DatabaseMcpServer.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class JsonParameterTests
{
    [Theory]
    [InlineData("1e-50", 1e-50)]
    [InlineData("-1e-50", -1e-50)]
    [InlineData("1e-29", 1e-29)]
    [InlineData("5e-324", double.Epsilon)]
    [InlineData("1.7976931348623157e308", double.MaxValue)]
    public void Binder_PreservesFloatingPointRangeWithoutDecimalUnderflow(string jsonNumber, double expected)
    {
        var parameters = SqlParameterBinder.Bind([new() { Name = "value", Value = JsonSerializer.Deserialize<JsonElement>(jsonNumber) }, new() { Name = "array", Value = JsonSerializer.Deserialize<JsonElement>("[" + jsonNumber + "]") }], SqlSugar.DbType.Sqlite);
        Assert.Equal(expected, Assert.IsType<double>(parameters[0].Value));
        Assert.Equal(expected, Assert.IsType<double>(Assert.Single(Assert.IsType<object[]>(parameters[1].Value))));
    }

    [Theory]
    [InlineData("1e-28", "0.0000000000000000000000000001")]
    [InlineData("1.2300e2", "123")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    public void Binder_PreservesExactDecimalBoundaries(string jsonNumber, string expected)
    {
        var parameter = Assert.Single(SqlParameterBinder.Bind([new() { Name = "value", Value = JsonSerializer.Deserialize<JsonElement>(jsonNumber) }], SqlSugar.DbType.Sqlite));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<decimal>(parameter.Value));
    }

    [Theory]
    [InlineData("1e-400")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("79228162514264337593543950336")]
    public void Binder_RejectsNumbersThatWouldChangeDuringConversion(string jsonNumber)
    {
        var exception = Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind([new() { Name = "value", Value = JsonSerializer.Deserialize<JsonElement>(jsonNumber) }], SqlSugar.DbType.Sqlite));
        Assert.Equal(DatabaseErrorCode.InvalidParameters, exception.ErrorCode);
    }

    [Fact]
    public void Binder_PreservesIntegerTypeAndPrecisionAboveDoubleRange()
    {
        var parameters = SqlParameterBinder.Bind(JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"id","value":9007199254740993},{"name":"small","value":41},{"name":"min","value":-9223372036854775808},{"name":"max","value":9223372036854775807}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, SqlSugar.DbType.Sqlite);
        Assert.Equal(9007199254740993L, Assert.IsType<long>(parameters[0].Value));
        Assert.Equal(41L, Assert.IsType<long>(parameters[1].Value));
        Assert.Equal(long.MinValue, Assert.IsType<long>(parameters[2].Value));
        Assert.Equal(long.MaxValue, Assert.IsType<long>(parameters[3].Value));
    }

    [Fact]
    public void Binder_PreservesDecimalAndArrayValuesWithoutRounding()
    {
        var parameters = SqlParameterBinder.Bind(JsonSerializer.Deserialize<DatabaseParameter[]>("""[{"name":"amount","value":12345678901234.5678},{"name":"ids","value":[9007199254740993,2]},{"name":"optional","value":null,"dbType":"Int32"},{"name":"large","value":1e+100}]""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!, SqlSugar.DbType.Sqlite);
        Assert.Equal(12345678901234.5678m, Assert.IsType<decimal>(parameters[0].Value));
        var ids = Assert.IsType<object[]>(parameters[1].Value);
        Assert.Equal(9007199254740993L, Assert.IsType<long>(ids[0]));
        Assert.Equal(2L, Assert.IsType<long>(ids[1]));
        Assert.Null(parameters[2].Value);
        Assert.Equal(1e100, Assert.IsType<double>(parameters[3].Value));
    }
}
