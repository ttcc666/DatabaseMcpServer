using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ColumnDefinition
{
    public required string Name { get; init; }
    [Description("Provider SQL type name without length, e.g. varchar, decimal, integer.")]
    public required string DataType { get; init; }
    public int? Length { get; init; }
    public byte? Precision { get; init; }
    public byte? Scale { get; init; }
    public bool Nullable { get; init; } = true;
    public bool PrimaryKey { get; init; }
    public bool Identity { get; init; }
    public string? Comment { get; init; }
    [Description("SQL default expression; applied only by schema-admin operations.")]
    public string? DefaultExpression { get; init; }
}
