namespace DatabaseMcpServer.Tools;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ToolGroupAttribute(string category, string provider, DatabaseMcpServer.Helpers.DatabaseToolAccess access) : Attribute
{
    public string Category { get; } = category;
    public string Provider { get; } = provider;
    public DatabaseMcpServer.Helpers.DatabaseToolAccess Access { get; } = access;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ToolPolicyAttribute : Attribute
{
    public bool RequiresConfirmation { get; init; }
    public bool Analysis { get; init; }
    public DatabaseMcpServer.Helpers.DatabaseCapability Capability { get; init; } = (DatabaseMcpServer.Helpers.DatabaseCapability)(-1);
    public DatabaseMcpServer.Helpers.DatabaseToolAccess Access { get; init; } = (DatabaseMcpServer.Helpers.DatabaseToolAccess)(-1);
}
