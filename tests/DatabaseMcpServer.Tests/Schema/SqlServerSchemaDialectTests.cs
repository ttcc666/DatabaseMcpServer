using DatabaseMcpServer.Models;
using DatabaseMcpServer.Schema;

namespace DatabaseMcpServer.Tests;

public class SqlServerSchemaDialectTests
{
    [Theory]
    [InlineData("orders", "dbo", "orders")]
    [InlineData("reporting.orders", "reporting", "orders")]
    public void ParseObjectName_PreservesExplicitSchema(string input, string schema, string table)
        => Assert.Equal((schema, table), SqlServerSchemaDialect.ParseObjectName(input));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("dbo.")]
    [InlineData(".orders")]
    [InlineData("dbo..orders")]
    [InlineData("db.dbo.orders")]
    [InlineData("orders;DROP TABLE other")]
    public void ParseObjectName_RejectsAmbiguousOrUnsafeNames(string input)
        => Assert.Equal(DatabaseErrorCode.InvalidParameters,
            Assert.Throws<DatabaseMcpException>(() => SqlServerSchemaDialect.ParseObjectName(input)).ErrorCode);

    [Fact]
    public void ParseObjectName_AcceptsMaximumIdentifierLengthAndRejectsLongerSegments()
    {
        var maximum = new string('a', 128);
        Assert.Equal((maximum, maximum), SqlServerSchemaDialect.ParseObjectName(maximum + "." + maximum));
        foreach (var input in new[] { maximum + "a", maximum + "a.orders", "dbo." + maximum + "a" })
            Assert.Equal(DatabaseErrorCode.InvalidParameters,
                Assert.Throws<DatabaseMcpException>(() => SqlServerSchemaDialect.ParseObjectName(input)).ErrorCode);
    }
}
