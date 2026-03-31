using DatabaseMcpServer.Models;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Helpers;

internal static class SchemaColumnDefinitionParser
{
    public static DbColumnInfo Parse(string columnInfo)
    {
        var columnData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(columnInfo);
        if (columnData == null)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的列信息 JSON");
        }

        var dataType = JsonElementValueConverter.GetString(columnData, "DataType") ?? "varchar";
        var column = new DbColumnInfo
        {
            DbColumnName = JsonElementValueConverter.GetString(columnData, "DbColumnName") ?? string.Empty,
            DataType = dataType,
            IsNullable = JsonElementValueConverter.GetBoolean(columnData, "IsNullable", true)
        };

        if (RequiresLength(dataType))
        {
            column.Length = JsonElementValueConverter.GetInt32(columnData, "Length", GetDefaultLength(dataType));
        }

        if (RequiresDecimalDigits(dataType))
        {
            column.Length = JsonElementValueConverter.GetInt32(columnData, "Length", 18);
            column.DecimalDigits = JsonElementValueConverter.GetInt32(columnData, "DecimalDigits", 2);
        }

        return column;
    }

    private static bool RequiresLength(string dataType)
    {
        var type = dataType.ToLowerInvariant();
        string[] stringTypes = ["char", "varchar", "nchar", "nvarchar", "binary", "varbinary"];
        return stringTypes.Any(t => type.Contains(t, StringComparison.Ordinal));
    }

    private static bool RequiresDecimalDigits(string dataType)
    {
        var type = dataType.ToLowerInvariant();
        return type.Contains("decimal", StringComparison.Ordinal)
            || type.Contains("numeric", StringComparison.Ordinal);
    }

    private static int GetDefaultLength(string dataType)
    {
        var type = dataType.ToLowerInvariant();

        return type switch
        {
            var t when t.Contains("nchar", StringComparison.Ordinal) && !t.Contains("nvarchar", StringComparison.Ordinal) => 10,
            var t when t.Contains("char", StringComparison.Ordinal)
                && !t.Contains("varchar", StringComparison.Ordinal)
                && !t.Contains("nchar", StringComparison.Ordinal) => 10,
            var t when t.Contains("nvarchar", StringComparison.Ordinal) => 50,
            var t when t.Contains("varchar", StringComparison.Ordinal) => 50,
            var t when t.Contains("varbinary", StringComparison.Ordinal) => 50,
            var t when t.Contains("binary", StringComparison.Ordinal) && !t.Contains("varbinary", StringComparison.Ordinal) => 8,
            _ => 255
        };
    }
}
