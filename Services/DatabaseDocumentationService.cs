using System.Reflection;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库文档生成服务
/// </summary>
internal class DatabaseDocumentationService : IDatabaseDocumentationService
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly ILogger<DatabaseDocumentationService> _logger;

    public DatabaseDocumentationService(IDatabaseConfigService databaseConfig, ILogger<DatabaseDocumentationService> logger)
    {
        _databaseConfig = databaseConfig;
        _logger = logger;
    }

    public DatabaseDocumentation GenerateDocumentation(string? connectionName = null, IReadOnlyCollection<string>? tableFilters = null)
    {
        var filterSet = CreateFilterSet(tableFilters);

        using var db = string.IsNullOrWhiteSpace(connectionName)
            ? _databaseConfig.CreateClient()
            : _databaseConfig.CreateClient(connectionName);

        var documentation = new DatabaseDocumentation
        {
            ConnectionName = string.IsNullOrWhiteSpace(connectionName)
                ? _databaseConfig.GetCurrentDatabaseName()
                : connectionName!,
            DatabaseType = db.CurrentConnectionConfig.DbType.ToString(),
            DatabaseName = db.Ado?.Connection?.Database,
            GeneratedAtUtc = DateTime.UtcNow
        };

        var tables = db.DbMaintenance.GetTableInfoList(false) ?? new List<DbTableInfo>();
        foreach (var table in tables)
        {
            if (filterSet != null && !filterSet.Contains(table.Name))
            {
                continue;
            }

            var tableDoc = BuildTableDocumentation(db, table, documentation.Warnings);
            documentation.Tables.Add(tableDoc);
        }

        documentation.Views.AddRange(TryGetViews(db, documentation.Warnings));

        return documentation;
    }

    private static HashSet<string>? CreateFilterSet(IReadOnlyCollection<string>? tableFilters)
    {
        if (tableFilters == null || tableFilters.Count == 0)
        {
            return null;
        }

        return new HashSet<string>(tableFilters.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
    }

    private TableDocumentation BuildTableDocumentation(ISqlSugarClient db, DbTableInfo table, List<string> warnings)
    {
        var columns = db.DbMaintenance.GetColumnInfosByTableName(table.Name, false) ?? new List<DbColumnInfo>();

        var indexes = TryGetIndexes(db, table.Name, warnings);
        var triggers = TryGetTriggers(db, table.Name, warnings);

        var createdTime = GetDateTimeProperty(table, "CreateTime");

        return new TableDocumentation
        {
            Name = table.Name,
            Description = NullIfEmpty(table.Description),
            CreatedTime = createdTime,
            Columns = columns.Select(MapColumn).ToList(),
            Indexes = indexes.ToList(),
            Triggers = triggers.ToList()
        };
    }

    private IEnumerable<IndexDocumentation> TryGetIndexes(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        try
        {
            var indexes = db.DbMaintenance.GetIndexList(tableName);
            return indexes.Select(MapIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取表 {TableName} 的索引失败", tableName);
            warnings.Add($"未能获取表 {tableName} 的索引: {ex.Message}");
            return Enumerable.Empty<IndexDocumentation>();
        }
    }

    private IEnumerable<string> TryGetTriggers(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        try
        {
            return db.DbMaintenance.GetTriggerNames(tableName) ?? Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取表 {TableName} 的触发器失败", tableName);
            warnings.Add($"未能获取表 {tableName} 的触发器: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }

    private IEnumerable<ViewDocumentation> TryGetViews(ISqlSugarClient db, List<string> warnings)
    {
        try
        {
            var views = db.DbMaintenance.GetViewInfoList();
            return views.Select(MapView);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取视图信息失败");
            warnings.Add($"未能获取视图信息: {ex.Message}");
            return Enumerable.Empty<ViewDocumentation>();
        }
    }

    private static ColumnDocumentation MapColumn(DbColumnInfo column)
    {
        return new ColumnDocumentation
        {
            Name = column.DbColumnName,
            DataType = column.DataType,
            Length = column.Length > 0 ? column.Length : null,
            Scale = column.DecimalDigits > 0 ? column.DecimalDigits : null,
            IsNullable = column.IsNullable,
            IsPrimaryKey = column.IsPrimarykey,
            IsIdentity = column.IsIdentity,
            DefaultValue = NullIfEmpty(column.DefaultValue),
            Description = NullIfEmpty(column.ColumnDescription)
        };
    }

    private static IndexDocumentation MapIndex(object index)
    {
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

    private static ViewDocumentation MapView(object view)
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

    private static DateTime? GetDateTimeProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string s when DateTime.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
