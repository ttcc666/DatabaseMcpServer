using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TableFilterOperator>))]
public enum TableFilterOperator
{
    [JsonStringEnumMemberName("eq")] Eq,
    [JsonStringEnumMemberName("ne")] Ne,
    [JsonStringEnumMemberName("gt")] Gt,
    [JsonStringEnumMemberName("gte")] Gte,
    [JsonStringEnumMemberName("lt")] Lt,
    [JsonStringEnumMemberName("lte")] Lte,
    [JsonStringEnumMemberName("isNull")] IsNull,
    [JsonStringEnumMemberName("isNotNull")] IsNotNull
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TableFilter
{
    public required string Column { get; init; }
    public required TableFilterOperator Op { get; init; }
    [Description("Scalar comparison value. For null checks use isNull/isNotNull without value or dbType.")]
    public JsonElement Value { get; init; }
    [Description("Optional scalar conversion type, following query_database parameter conversion rules.")]
    public DatabaseParameterType? DbType { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TableSortDirection>))]
public enum TableSortDirection
{
    [JsonStringEnumMemberName("asc")] Asc,
    [JsonStringEnumMemberName("desc")] Desc
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TableOrder
{
    public required string Column { get; init; }
    public TableSortDirection Direction { get; init; }
}
