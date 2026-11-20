using System.Data;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class TypedParameterTests
{
    [Fact]
    public void Errors_ClassifyUnsupportedInvalidJsonAndProviderCodesWithoutMessageMatching()
    {
        void CheckError(Exception error, int expected)
        {
            var result = DatabaseMcpServer.Services.DatabaseErrorMapper.Map(error, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            Assert.False(result.Success);
            Assert.Equal(expected, (int)result.ErrorCode!);
        }
        CheckError(new NotSupportedException("secret"), 1012);
        CheckError(new JsonException("secret"), 1004);
        CheckError(new Npgsql.NpgsqlException("opaque", new TimeoutException()), 1011);
        CheckError(new Npgsql.PostgresException("opaque", "ERROR", "ERROR", "28P01"), 1001);
        CheckError(new Npgsql.PostgresException("timeout is just table text", "ERROR", "ERROR", "42P01"), 1002);
    }
    [Theory]
    [InlineData(DatabaseParameterType.Int16, "123", typeof(short))]
    [InlineData(DatabaseParameterType.Int32, "123", typeof(int))]
    [InlineData(DatabaseParameterType.Int64, "9007199254740993", typeof(long))]
    [InlineData(DatabaseParameterType.Decimal, "12345678901234.5678", typeof(decimal))]
    [InlineData(DatabaseParameterType.Double, "1e-50", typeof(double))]
    [InlineData(DatabaseParameterType.Single, "1.25", typeof(float))]
    [InlineData(DatabaseParameterType.Boolean, "true", typeof(bool))]
    [InlineData(DatabaseParameterType.Guid, "\"4b34dbf3-8dc2-4ae4-9296-168a7fdf125f\"", typeof(Guid))]
    [InlineData(DatabaseParameterType.Binary, "\"AP8=\"", typeof(byte[]))]
    [InlineData(DatabaseParameterType.Date, "\"2026-11-20\"", typeof(DateTime))]
    [InlineData(DatabaseParameterType.DateTimeOffset, "\"2026-11-20T08:00:00+08:00\"", typeof(DateTimeOffset))]
    public void Binder_PreservesExactClrTypeAndValue(DatabaseParameterType type, string value, Type clrType)
    {
        var bound = Assert.Single(SqlParameterBinder.Bind( [Parameter(type, value)], SqlSugar.DbType.SqlServer));
        Assert.IsType(clrType, bound.Value);
        Assert.Equal(Enum.Parse<DbType>(type.ToString()), bound.DbType);
        if (type is DatabaseParameterType.Int64 or DatabaseParameterType.Decimal or DatabaseParameterType.Double or DatabaseParameterType.Single)
            Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(value), JsonSerializer.SerializeToElement(bound.Value, clrType)));
        if (type == DatabaseParameterType.DateTimeOffset) Assert.Equal(TimeSpan.FromHours(8), ((DateTimeOffset)bound.Value).Offset);
        if (type == DatabaseParameterType.Binary) Assert.Equal(new byte[] { 0, 255 }, (byte[])bound.Value);
    }

    [Theory]
    [InlineData(DatabaseParameterType.Int16, "32768")]
    [InlineData(DatabaseParameterType.Int64, "1.5")]
    [InlineData(DatabaseParameterType.Decimal, "1e-50")]
    [InlineData(DatabaseParameterType.Double, "9007199254740993")]
    [InlineData(DatabaseParameterType.Single, "1.2345678901234567")]
    [InlineData(DatabaseParameterType.DateTimeOffset, "\"2026-11-20T08:00:00\"")]
    [InlineData(DatabaseParameterType.Date, "\"20/11/2026\"")]
    [InlineData(DatabaseParameterType.String, "42")]
    public void Binder_RejectsRoundingOverflowAndAmbiguousDates(DatabaseParameterType type, string value)
        => Assert.Equal(DatabaseErrorCode.InvalidParameters, Assert.Throws<DatabaseMcpException>(() =>
            SqlParameterBinder.Bind( [Parameter(type, value)], SqlSugar.DbType.SqlServer)).ErrorCode);

    [Fact]
    public void Binder_ValidatesNullOutputSizePrecisionAndProviderCapabilities()
    {
        var typedNull = Parameter(DatabaseParameterType.Int64, "null");
        var bound = Assert.Single(SqlParameterBinder.Bind( [typedNull], SqlSugar.DbType.PostgreSQL));
        Assert.Null(bound.Value);
        Assert.Equal(DbType.Int64, bound.DbType);
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [new() { Name = "v", Value = JsonSerializer.SerializeToElement<object?>(null) }], SqlSugar.DbType.PostgreSQL));
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [typedNull, new() { Name = "@v", DbType = DatabaseParameterType.Int64 }], SqlSugar.DbType.SqlServer));
        var output = new DatabaseParameter { Name = "v", DbType = DatabaseParameterType.String, Direction = DatabaseParameterDirection.Output, Size = 32 };
        var outBound = Assert.Single(SqlParameterBinder.Bind( [output], SqlSugar.DbType.MySql, true));
        Assert.Equal(ParameterDirection.Output, outBound.Direction);
        Assert.Equal(32, outBound.Size);
        Assert.Equal(DatabaseErrorCode.UnsupportedCapability, Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [output], SqlSugar.DbType.PostgreSQL, true)).ErrorCode);
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [new() { Name = "v", DbType = DatabaseParameterType.String, Direction = DatabaseParameterDirection.Output }], SqlSugar.DbType.MySql, true));
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [new() { Name = "v", DbType = DatabaseParameterType.String, Value = JsonSerializer.SerializeToElement("hello"), Size = 3 }], SqlSugar.DbType.MySql));
        var decimalParameter = new DatabaseParameter { Name = "v", DbType = DatabaseParameterType.Decimal, Value = JsonSerializer.SerializeToElement(12.345m), Precision = 5, Scale = 2 };
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [decimalParameter], SqlSugar.DbType.SqlServer));
        Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind( [new() { Name = "v", DbType = DatabaseParameterType.Decimal, Value = JsonSerializer.SerializeToElement(123.45m), Precision = 4, Scale = 2 }], SqlSugar.DbType.SqlServer));
        var inputOutput = new DatabaseParameter { Name = "v", DbType = DatabaseParameterType.Decimal, Value = JsonSerializer.SerializeToElement(12.34m), Precision = 5, Scale = 2 };
        var inout = Assert.Single(SqlParameterBinder.Bind( [inputOutput], SqlSugar.DbType.SqlServer, true));
        Assert.Equal(12.34m, inout.Value);
        Assert.Equal((byte)2, inout.Scale);
        Assert.Equal(ParameterDirection.Input, inout.Direction);
        Assert.Equal(DatabaseErrorCode.UnsupportedCapability, Assert.Throws<DatabaseMcpException>(() => SqlParameterBinder.Bind(
            [new() { Name = "v", DbType = DatabaseParameterType.Decimal, Precision = 5, Scale = 2, Direction = DatabaseParameterDirection.Output }], SqlSugar.DbType.SqlServer, true)).ErrorCode);
    }

    private static DatabaseParameter Parameter(DatabaseParameterType type, string value)
        => new() { Name = "v", DbType = type, Value = JsonSerializer.Deserialize<JsonElement>(value) };
}
