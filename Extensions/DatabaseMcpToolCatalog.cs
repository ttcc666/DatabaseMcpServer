using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Documentation;
using DatabaseMcpServer.Tools.Export;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Extensions;

internal sealed record DatabaseMcpToolRegistration(
    Type ToolType,
    Func<IMcpServerBuilder, IMcpServerBuilder> RegisterWithMcp);

internal static class DatabaseMcpToolCatalog
{
    public static IReadOnlyList<DatabaseMcpToolRegistration> Registrations { get; } =
    [
        new(typeof(ConnectionTools), static builder => builder.WithTools<ConnectionTools>()),
        new(typeof(SchemaTools), static builder => builder.WithTools<SchemaTools>()),
        new(typeof(QueryTools), static builder => builder.WithTools<QueryTools>()),
        new(typeof(CommandTools), static builder => builder.WithTools<CommandTools>()),
        new(typeof(ExcelExportTools), static builder => builder.WithTools<ExcelExportTools>()),
        new(typeof(DocumentationTools), static builder => builder.WithTools<DocumentationTools>())
    ];

    public static IReadOnlyList<Type> ToolTypes { get; } = Registrations
        .Select(item => item.ToolType)
        .ToArray();
}
