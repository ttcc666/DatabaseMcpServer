namespace DatabaseMcpServer.Models;

public sealed record ConfigurationMutationResult(
    bool Success, bool Saved, bool RuntimeApplied, bool? SelectionPersisted,
    string Message, string? CurrentDatabase = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigPath { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DatabaseName { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public object? Database { get; init; }
}
