using System.Reflection;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 数据库文档生成策略接口
/// </summary>
public interface IDatabaseDocumentationStrategy
{
    IEnumerable<IndexDocumentation> GetIndexes(ISqlSugarClient db, string tableName, IReadOnlyList<DbColumnInfo> columns, List<string> warnings);
    IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings);
    TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings);
    string? GetTableDdl(ISqlSugarClient db, string tableName, List<string> warnings);
    IEnumerable<string> GetTriggers(ISqlSugarClient db, string tableName, List<string> warnings);
    IEnumerable<ViewDocumentation> GetViews(ISqlSugarClient db, List<string> warnings);
}

/// <summary>
/// 文档生成策略基类，优先使用 SqlSugar 内置能力，不支持的部分交由具体数据库策略覆盖
/// </summary>
public class DatabaseDocumentationStrategyBase : IDatabaseDocumentationStrategy
{
    protected readonly ILogger? Logger;

    public DatabaseDocumentationStrategyBase(ILogger? logger = null)
    {
        Logger = logger;
    }

    public virtual IEnumerable<IndexDocumentation> GetIndexes(ISqlSugarClient db, string tableName, IReadOnlyList<DbColumnInfo> columns, List<string> warnings)
    {
        try
        {
            var indexes = db.DbMaintenance.GetIndexList(tableName) ?? new List<string>();
            var mapped = indexes.Select(MapIndex).ToList();
            return MergeIndexes(mapped, columns);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的索引失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的索引: {ex.Message}");
            return Enumerable.Empty<IndexDocumentation>();
        }
    }

    public virtual IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {GetDbTypeName(db)} 暂不支持外键信息提取，已跳过。");
        return Enumerable.Empty<ForeignKeyDocumentation>();
    }

    public virtual TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {GetDbTypeName(db)} 暂不支持统计/容量信息提取，已跳过。");
        return null;
    }

    public virtual string? GetTableDdl(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {GetDbTypeName(db)} 暂不支持 DDL 摘要提取，已跳过。");
        return null;
    }

    public virtual IEnumerable<string> GetTriggers(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        try
        {
            return db.DbMaintenance.GetTriggerNames(tableName) ?? Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的触发器失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的触发器: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }

    public virtual IEnumerable<ViewDocumentation> GetViews(ISqlSugarClient db, List<string> warnings)
    {
        try
        {
            var views = db.DbMaintenance.GetViewInfoList();
            return views.Select(MapView);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取视图信息失败");
            AddWarningOnce(warnings, $"未能获取视图信息: {ex.Message}");
            return Enumerable.Empty<ViewDocumentation>();
        }
    }

    protected static IEnumerable<ForeignKeyDocumentation> GroupForeignKeys(IEnumerable<ForeignKeyRow> rows)
    {
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ReferencedTable))
            .GroupBy(r => new
            {
                Name = string.IsNullOrWhiteSpace(r.ConstraintName) ? "(unknown)" : r.ConstraintName,
                RefTable = string.IsNullOrWhiteSpace(r.ReferencedTable) ? "(unknown)" : r.ReferencedTable,
                UpdateRule = NormalizeRule(r.UpdateRule),
                DeleteRule = NormalizeRule(r.DeleteRule)
            })
            .Select(group =>
            {
                var columns = group
                    .Select(r => r.ColumnName)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var referencedColumns = group
                    .Select(r => r.ReferencedColumn)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ForeignKeyDocumentation
                {
                    Name = group.Key.Name,
                    Columns = columns,
                    ReferencedTable = group.Key.RefTable,
                    ReferencedColumns = referencedColumns,
                    UpdateRule = group.Key.UpdateRule,
                    DeleteRule = group.Key.DeleteRule
                };
            });
    }

    protected static IndexDocumentation MapIndex(object index)
    {
        if (index is string nameString)
        {
            return new IndexDocumentation
            {
                Name = string.IsNullOrWhiteSpace(nameString) ? "(unknown)" : nameString.Trim(),
                Columns = new List<string>()
            };
        }

        var name = GetStringProperty(index, "IndexName") ?? GetStringProperty(index, "Name") ?? "(unknown)";
        var type = GetStringProperty(index, "IndexType");
        var description = GetStringProperty(index, "Description") ?? GetStringProperty(index, "IndexDescription");
        var isUnique = GetBoolProperty(index, "IsUnique") ?? false;
        var columnsRaw = GetPropertyValue(index, "IndexColumns")
                         ?? GetPropertyValue(index, "IndexKeys")
                         ?? GetPropertyValue(index, "ColumnName");

        return new IndexDocumentation
        {
            Name = name,
            IsUnique = isUnique,
            Type = type,
            Columns = NormalizeColumns(columnsRaw),
            Description = description
        };
    }

    protected static IEnumerable<IndexDocumentation> MergeIndexes(IEnumerable<IndexDocumentation> indexes, IReadOnlyList<DbColumnInfo> columns)
    {
        var primaryKeyColumns = columns
            .Where(c => c.IsPrimarykey && !string.IsNullOrWhiteSpace(c.DbColumnName))
            .Select(c => c.DbColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return indexes
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Name) ? "(unknown)" : i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var mergedColumns = group
                    .SelectMany(i => i.Columns ?? Enumerable.Empty<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mergedColumns.Count == 0 && IsPrimaryKeyName(group.Key) && primaryKeyColumns.Count > 0)
                {
                    mergedColumns = primaryKeyColumns;
                }

                return new IndexDocumentation
                {
                    Name = group.Key,
                    IsUnique = group.Any(i => i.IsUnique) || (IsPrimaryKeyName(group.Key) && primaryKeyColumns.Count > 0),
                    Type = group.Select(i => i.Type).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                    Description = group.Select(i => i.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)),
                    Columns = mergedColumns
                };
            });
    }

    protected static ViewDocumentation MapView(object view)
    {
        var name = GetStringProperty(view, "Name")
                   ?? GetStringProperty(view, "ViewName")
                   ?? "(unknown)";

        return new ViewDocumentation
        {
            Name = name,
            Description = NullIfEmpty(GetStringProperty(view, "Description")),
            Definition = NullIfEmpty(GetStringProperty(view, "Definition") ?? GetStringProperty(view, "Script"))
        };
    }

    protected static string GetDbTypeName(ISqlSugarClient db)
    {
        return db.CurrentConnectionConfig?.DbType.ToString() ?? "Unknown";
    }

    protected static void AddWarningOnce(List<string> warnings, string message)
    {
        if (!warnings.Contains(message))
        {
            warnings.Add(message);
        }
    }

    private static List<string> NormalizeColumns(object? columnsRaw)
    {
        if (columnsRaw == null)
        {
            return new List<string>();
        }

        if (columnsRaw is IEnumerable<string> stringEnumerable)
        {
            return stringEnumerable.Select(c => c?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList()!;
        }

        if (columnsRaw is string columnString)
        {
            return columnString
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        if (columnsRaw is IEnumerable<object> objectEnumerable)
        {
            return objectEnumerable
                .Select(o => o?.ToString()?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList()!;
        }

        var single = columnsRaw.ToString();
        return string.IsNullOrWhiteSpace(single)
            ? new List<string>()
            : new List<string> { single };
    }

    private static bool IsPrimaryKeyName(string name)
    {
        return name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("PK", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetStringProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value?.ToString();
    }

    private static bool? GetBoolProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            int i => i != 0,
            long l => l != 0,
            _ => null
        };
    }

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(instance);
    }

    private static string? NormalizeRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        var normalized = rule.Trim().ToUpperInvariant();
        return normalized switch
        {
            "NO ACTION" => "NO ACTION",
            "CASCADE" => "CASCADE",
            "SET NULL" => "SET NULL",
            "SET DEFAULT" => "SET DEFAULT",
            "RESTRICT" => "RESTRICT",
            _ => normalized
        };
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    protected sealed class ForeignKeyRow
    {
        public string? ConstraintName { get; set; }
        public string? ColumnName { get; set; }
        public string? ReferencedTable { get; set; }
        public string? ReferencedColumn { get; set; }
        public string? UpdateRule { get; set; }
        public string? DeleteRule { get; set; }
    }
}
