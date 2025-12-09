using DatabaseMcpServer.Models;
using System.Text;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 将数据库文档模型转换为 Markdown
/// </summary>
internal static class DocumentationMarkdownFormatter
{
    /// <summary>
    /// 将数据库文档对象转换为 Markdown 字符串。
    /// </summary>
    public static string ToMarkdown(DatabaseDocumentation documentation)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# 数据库文档 - {documentation.ConnectionName}");
        sb.AppendLine();
        sb.AppendLine($"- 数据库类型: {documentation.DatabaseType}");
        if (!string.IsNullOrWhiteSpace(documentation.DatabaseName))
        {
            sb.AppendLine($"- 数据库名称: {documentation.DatabaseName}");
        }
        sb.AppendLine($"- 生成时间 (UTC): {documentation.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- 表数量: {documentation.Tables.Count}");
        if (documentation.Views.Count > 0)
        {
            sb.AppendLine($"- 视图数量: {documentation.Views.Count}");
        }
        if (documentation.Warnings.Any())
        {
            sb.AppendLine("- 警告:");
            foreach (var warning in documentation.Warnings)
            {
                sb.AppendLine($"  - {warning}");
            }
        }

        sb.AppendLine();

        foreach (var table in documentation.Tables.OrderBy(t => t.Name))
        {
            AppendTableSection(sb, table);
        }

        if (documentation.Views.Count > 0)
        {
            sb.AppendLine("# 视图");
            foreach (var view in documentation.Views.OrderBy(v => v.Name))
            {
                sb.AppendLine($"## 视图 {view.Name}");
                if (!string.IsNullOrWhiteSpace(view.Description))
                {
                    sb.AppendLine($"> {view.Description}");
                }
                if (!string.IsNullOrWhiteSpace(view.Definition))
                {
                    sb.AppendLine();
                    sb.AppendLine("```sql");
                    sb.AppendLine(view.Definition);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 为单个表构建 Markdown 段落。
    /// </summary>
    private static void AppendTableSection(StringBuilder sb, TableDocumentation table)
    {
        sb.AppendLine($"## 表 {table.Name}");
        if (!string.IsNullOrWhiteSpace(table.Description))
        {
            sb.AppendLine($"> {table.Description}");
        }
        if (table.CreatedTime.HasValue)
        {
            sb.AppendLine($"- 创建时间: {table.CreatedTime.Value:yyyy-MM-dd HH:mm:ss}");
        }
        if (table.Statistics is not null)
        {
            var stats = table.Statistics;
            var rowCount = stats.RowCount?.ToString() ?? "-";
            var dataSize = FormatBytes(stats.DataBytes);
            var indexSize = FormatBytes(stats.IndexBytes);
            var totalSize = FormatBytes(stats.TotalBytes);
            sb.AppendLine($"- 数据量: 行数 {rowCount}, 数据 {dataSize}, 索引 {indexSize}, 总计 {totalSize}");
        }

        sb.AppendLine();
        sb.AppendLine("| 列名 | 类型 | 可空 | 主键 | 自增 | 默认值 | 说明 |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var column in table.Columns.OrderBy(c => c.Name))
        {
            var typeDisplay = BuildTypeDisplay(column);
            var defaultValue = string.IsNullOrWhiteSpace(column.DefaultValue) ? "-" : column.DefaultValue;
            var description = string.IsNullOrWhiteSpace(column.Description) ? "-" : column.Description;
            sb.AppendLine($"| {column.Name} | {typeDisplay} | {(column.IsNullable ? "是" : "否")} | {(column.IsPrimaryKey ? "是" : "-")} | {(column.IsIdentity ? "是" : "-")} | {defaultValue} | {description} |");
        }

        if (table.Indexes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**索引**");
            foreach (var index in table.Indexes.OrderBy(i => i.Name))
            {
                var columns = index.Columns.Count > 0 ? string.Join(", ", index.Columns) : "(未提供列信息)";
                var desc = string.IsNullOrWhiteSpace(index.Description) ? string.Empty : $" - {index.Description}";
                var type = string.IsNullOrWhiteSpace(index.Type) ? string.Empty : $" ({index.Type})";
                sb.AppendLine($"- {index.Name}{type} {(index.IsUnique ? "[唯一]" : string.Empty)}: {columns}{desc}");
            }
        }

        if (table.ForeignKeys.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**外键**");
            foreach (var fk in table.ForeignKeys.OrderBy(f => f.Name))
            {
                var columns = fk.Columns.Count > 0 ? string.Join(", ", fk.Columns) : "(未提供列信息)";
                var refColumns = fk.ReferencedColumns.Count > 0 ? string.Join(", ", fk.ReferencedColumns) : "(未提供列信息)";
                var rules = BuildForeignKeyRuleDisplay(fk);
                var desc = string.IsNullOrWhiteSpace(fk.Description) ? string.Empty : $" - {fk.Description}";
                sb.AppendLine($"- {fk.Name}: {columns} => {fk.ReferencedTable} ({refColumns}){rules}{desc}");
            }
        }

        if (table.Triggers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**触发器**");
            foreach (var trigger in table.Triggers.OrderBy(t => t))
            {
                sb.AppendLine($"- {trigger}");
            }
        }

        if (!string.IsNullOrWhiteSpace(table.Ddl))
        {
            sb.AppendLine();
            sb.AppendLine("**DDL 摘要**");
            sb.AppendLine("```sql");
            sb.AppendLine(table.Ddl);
            sb.AppendLine("```");
        }

        sb.AppendLine();
    }

    private static string BuildTypeDisplay(ColumnDocumentation column)
    {
        if (column.Length.HasValue && column.Scale.HasValue)
        {
            return $"{column.DataType}({column.Length},{column.Scale})";
        }

        if (column.Length.HasValue)
        {
            return $"{column.DataType}({column.Length})";
        }

        return column.DataType;
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0)
        {
            return "-";
        }

        double value = bytes.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = 0;

        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##}{units[index]}";
    }

    private static string BuildForeignKeyRuleDisplay(ForeignKeyDocumentation fk)
    {
        if (string.IsNullOrWhiteSpace(fk.UpdateRule) && string.IsNullOrWhiteSpace(fk.DeleteRule))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fk.UpdateRule))
        {
            parts.Add($"更新:{fk.UpdateRule}");
        }
        if (!string.IsNullOrWhiteSpace(fk.DeleteRule))
        {
            parts.Add($"删除:{fk.DeleteRule}");
        }

        return parts.Count == 0 ? string.Empty : $" [{string.Join(", ", parts)}]";
    }
}