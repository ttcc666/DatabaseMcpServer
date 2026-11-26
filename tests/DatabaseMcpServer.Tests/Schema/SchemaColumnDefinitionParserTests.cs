using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class SchemaColumnDefinitionParserTests
{
    [Theory]
    [InlineData("decimal", 38, 2)]
    [InlineData("decimal", 39, 2)]
    [InlineData("decimal", 65, 30)]
    [InlineData("numeric", 65, 30)]
    public void Parse_PreservesProviderPrecision(string dataType, byte precision, byte scale)
    {
        var column = SchemaColumnDefinitionParser.Parse(new()
        {
            Name = "amount", DataType = dataType, Precision = precision, Scale = scale
        });

        Assert.Equal(dataType, column.DataType);
        Assert.Equal(precision, column.Length);
        Assert.Equal(scale, column.DecimalDigits);
    }

    [Theory]
    [InlineData((byte)0, (byte)0)]
    [InlineData((byte)18, (byte)19)]
    [InlineData((byte)18, null)]
    [InlineData(null, (byte)2)]
    public void Parse_RejectsInvalidPrecisionShape(byte? precision, byte? scale)
    {
        var error = Assert.Throws<DatabaseMcpException>(() => SchemaColumnDefinitionParser.Parse(new()
        {
            Name = "amount", DataType = "decimal", Precision = precision, Scale = scale
        }));

        Assert.Equal(DatabaseErrorCode.InvalidParameters, error.ErrorCode);
    }

    [Fact]
    public void Parse_ShouldApplyExpectedDefaults_ForDecimalColumn()
    {
        var column = SchemaColumnDefinitionParser.Parse(new() { Name = "amount", DataType = "decimal", Nullable = false });

        Assert.Equal("amount", column.DbColumnName);
        Assert.Equal("decimal", column.DataType);
        Assert.False(column.IsNullable);
        Assert.Equal(18, column.Length);
        Assert.Equal(2, column.DecimalDigits);
    }

    [Fact]
    public void ParseMany_ShouldParseCreateTableColumnDefinitions()
    {
        var columns = SchemaColumnDefinitionParser.ParseMany([new() { Name = "id", DataType = "int", PrimaryKey = true, Identity = true, Nullable = false },
            new() { Name = "name", DataType = "nvarchar", Length = 100, Comment = "Display name" }]);

        Assert.Equal(2, columns.Count);
        Assert.Equal("id", columns[0].DbColumnName);
        Assert.True(columns[0].IsPrimarykey);
        Assert.True(columns[0].IsIdentity);
        Assert.False(columns[0].IsNullable);
        Assert.Equal("name", columns[1].DbColumnName);
        Assert.Equal(100, columns[1].Length);
        Assert.Equal("Display name", columns[1].ColumnDescription);
    }

    [Fact]
    public void ParseMany_ShouldRejectEmptyColumnList()
    {
        Assert.Throws<DatabaseMcpException>(() => SchemaColumnDefinitionParser.ParseMany([]));
    }
}
