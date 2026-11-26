using System.ComponentModel;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Tools;

[AttributeUsage(AttributeTargets.Parameter)]
internal abstract class ToolChoicesDescriptionAttribute(string description, string[] choices) : DescriptionAttribute(description)
{
    public IReadOnlyList<string> Choices { get; } = choices;
}

internal sealed class SchemaSectionsDescriptionAttribute() : ToolChoicesDescriptionAttribute(
    $"Sections: {string.Join(", ", CapabilityNames<SchemaSectionKind>.All)}. Defaults: {string.Join(", ", ProviderCapabilities.DefaultSchemaSections.Select(CapabilityNames<SchemaSectionKind>.Name))}.",
    CapabilityNames<SchemaSectionKind>.All.ToArray());
