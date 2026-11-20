namespace DatabaseMcpServer.Tools;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ToolPolicyAttribute : Attribute
{
    public bool RequiresConfirmation { get; init; }
}
