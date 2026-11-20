using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DatabaseCommand
{
    [Description("SQL command to execute.")]
    public required string Sql { get; init; }

    [Description("Typed input parameter array; NULL requires dbType.")]
    public DatabaseParameter[]? Parameters { get; init; }
}
