namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库文档根节点
/// </summary>
public class DatabaseDocumentation
{
    public string ConnectionName { get; set; } = string.Empty;
    public string DatabaseType { get; set; } = string.Empty;
    public string? DatabaseName { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<TableDocumentation> Tables { get; set; } = new();
    public List<ViewDocumentation> Views { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 表文档
/// </summary>
public class TableDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? CreatedTime { get; set; }
    public List<ColumnDocumentation> Columns { get; set; } = new();
    public List<IndexDocumentation> Indexes { get; set; } = new();
    public List<string> Triggers { get; set; } = new();
}

/// <summary>
/// 列文档
/// </summary>
public class ColumnDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int? Length { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// 索引文档
/// </summary>
public class IndexDocumentation
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public string? Type { get; set; }
    public List<string> Columns { get; set; } = new();
    public string? Description { get; set; }
}

/// <summary>
/// 视图文档
/// </summary>
public class ViewDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string? Definition { get; set; }
    public string? Description { get; set; }
}
