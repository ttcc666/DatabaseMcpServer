using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class SchemaColumnDefinitionParserTests
{
    [Fact]
    public void Parse_ShouldApplyExpectedDefaults_ForDecimalColumn()
    {
        var column = SchemaColumnDefinitionParser.Parse("""{"DbColumnName":"amount","DataType":"decimal","IsNullable":"false"}""");

        Assert.Equal("amount", column.DbColumnName);
        Assert.Equal("decimal", column.DataType);
        Assert.False(column.IsNullable);
        Assert.Equal(18, column.Length);
        Assert.Equal(2, column.DecimalDigits);
    }

    [Fact]
    public void ParseMany_ShouldParseCreateTableColumnDefinitions()
    {
        var columns = SchemaColumnDefinitionParser.ParseMany("""[{"DbColumnName":"id","DataType":"int","IsPrimarykey":true,"IsIdentity":true,"IsNullable":false},{"DbColumnName":"name","DataType":"nvarchar","Length":100,"ColumnDescription":"Display name"}]""");

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
        Assert.Throws<DatabaseMcpException>(() => SchemaColumnDefinitionParser.ParseMany("[]"));
    }
}
