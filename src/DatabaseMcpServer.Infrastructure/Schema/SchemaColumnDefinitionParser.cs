using DatabaseMcpServer.Models;
using SqlSugar;
using System.Text.RegularExpressions;

namespace DatabaseMcpServer.Helpers;

internal static class SchemaColumnDefinitionParser
{
    public static DbColumnInfo Parse(ColumnDefinition column)
    {
        if (column == null || string.IsNullOrWhiteSpace(column.Name) || string.IsNullOrWhiteSpace(column.DataType))
            throw Invalid("列必须提供 name 和 dataType。");
        SqlSafetyGuard.EnsureSafeTableName(column.Name);
        if (column.Name.Contains('.') || !Regex.IsMatch(column.DataType, @"\A[A-Za-z][A-Za-z0-9_ ]{0,63}\z") ||
            column.Length is <= 0 || column.Precision is 0 || column.Scale > column.Precision ||
            column.Scale.HasValue != column.Precision.HasValue || column.Length.HasValue && column.Precision.HasValue)
            throw Invalid("字段类型、长度或 precision/scale 无效。");
        var type = column.DataType.ToLowerInvariant();
        var decimalType = type is "decimal" or "numeric";
        if (column.Precision.HasValue && !decimalType) throw Invalid("precision/scale 仅用于 decimal/numeric。");
        return new()
        {
            DbColumnName = column.Name, DataType = column.DataType,
            Length = column.Length ?? column.Precision ?? (decimalType ? 18 : type.Contains("char", StringComparison.Ordinal) ? 50 : 0),
            DecimalDigits = column.Scale ?? (decimalType ? 2 : 0),
            IsNullable = column.Nullable && !column.PrimaryKey, IsPrimarykey = column.PrimaryKey, IsIdentity = column.Identity,
            ColumnDescription = column.Comment ?? "", DefaultValue = column.DefaultExpression ?? ""
        };
    }

    public static List<DbColumnInfo> ParseMany(ColumnDefinition[] columns)
    {
        if (columns is not { Length: > 0 and <= 1024 }) throw Invalid("columns 必须包含 1-1024 个字段。");
        var mapped = columns.Select(Parse).ToList();
        if (mapped.Select(c => c.DbColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mapped.Count)
            throw Invalid("columns 包含重复名称。");
        for (var i = 0; i < mapped.Count; i++) mapped[i].CreateTableFieldSort = i;
        return mapped;
    }

    private static DatabaseMcpException Invalid(string message) => new(DatabaseErrorCode.InvalidParameters, message);
}
