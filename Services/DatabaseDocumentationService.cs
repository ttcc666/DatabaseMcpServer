using System.Reflection;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Strategies;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库文档生成服务
/// </summary>
internal class DatabaseDocumentationService : IDatabaseDocumentationService
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly DatabaseDocumentationStrategyFactory _strategyFactory;
    private readonly ILogger<DatabaseDocumentationService> _logger;

    public DatabaseDocumentationService(
        IDatabaseConfigService databaseConfig,
        DatabaseDocumentationStrategyFactory strategyFactory,
        ILogger<DatabaseDocumentationService> logger)
    {
        _databaseConfig = databaseConfig;
        _strategyFactory = strategyFactory;
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

        var strategy = _strategyFactory.GetStrategy(db.CurrentConnectionConfig.DbType);
        _logger.LogDebug("使用 {Strategy} 处理数据库文档生成，类型 {DbType}", strategy.GetType().Name, db.CurrentConnectionConfig.DbType);
        var tables = db.DbMaintenance.GetTableInfoList(false) ?? new List<DbTableInfo>();

        foreach (var table in tables)
        {
            if (filterSet != null && !filterSet.Contains(table.Name))
            {
                continue;
            }

            var tableDoc = BuildTableDocumentation(db, strategy, table, documentation.Warnings);
            documentation.Tables.Add(tableDoc);
        }

        documentation.Views.AddRange(strategy.GetViews(db, documentation.Warnings));

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

    private TableDocumentation BuildTableDocumentation(
        ISqlSugarClient db,
        IDatabaseDocumentationStrategy strategy,
        DbTableInfo table,
        List<string> warnings)
    {
        var columns = db.DbMaintenance.GetColumnInfosByTableName(table.Name, false) ?? new List<DbColumnInfo>();

        var indexes = strategy.GetIndexes(db, table.Name, columns, warnings);
        var foreignKeys = strategy.GetForeignKeys(db, table.Name, warnings);
        var triggers = strategy.GetTriggers(db, table.Name, warnings);
        var statistics = strategy.GetTableStatistics(db, table.Name, warnings);
        var ddl = strategy.GetTableDdl(db, table.Name, warnings);

        var createdTime = GetDateTimeProperty(table, "CreateTime");

        return new TableDocumentation
        {
            Name = table.Name,
            Description = NullIfEmpty(table.Description),
            CreatedTime = createdTime,
            Columns = columns.Select(MapColumn).ToList(),
            Indexes = indexes.ToList(),
            ForeignKeys = foreignKeys.ToList(),
            Triggers = triggers.ToList(),
            Statistics = statistics,
            Ddl = ddl
        };
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

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(instance);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
