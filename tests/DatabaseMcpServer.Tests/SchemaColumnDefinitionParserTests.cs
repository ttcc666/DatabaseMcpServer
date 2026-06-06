using DatabaseMcpServer.Helpers;

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
}
