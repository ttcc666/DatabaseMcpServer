using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

[JsonConverter(typeof(JsonStringEnumConverter<DatabaseParameterType>))]
public enum DatabaseParameterType
{
    String, AnsiString, Int16, Int32, Int64, Byte, Boolean, Decimal, Double, Single,
    Guid, Date, DateTime, DateTime2, DateTimeOffset, Time, Binary
}

[JsonConverter(typeof(JsonStringEnumConverter<DatabaseParameterDirection>))]
public enum DatabaseParameterDirection { Input, Output, InputOutput, ReturnValue }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DatabaseParameter
{
    [Description("SQL parameter name, optionally prefixed with @, : or $. Must be unique.")]
    public required string Name { get; init; }
    [Description("JSON value. Use ISO 8601 strings for dates, base64 for binary. Null requires dbType.")]
    public JsonElement Value { get; init; }
    [Description("Explicit ADO type; omit only for non-null input values. Arrays are input parameters.")]
    public DatabaseParameterType? DbType { get; init; }
    public DatabaseParameterDirection Direction { get; init; }
    [Description("Required positive size for string/binary output parameters.")]
    public int? Size { get; init; }
    [Description("Input Decimal validation only; requires scale. Current SqlSugar does not forward Precision to drivers; output precision is unsupported.")]
    public byte? Precision { get; init; }
    public byte? Scale { get; init; }
}
