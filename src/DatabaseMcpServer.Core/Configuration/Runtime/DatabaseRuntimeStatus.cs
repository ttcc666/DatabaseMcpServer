namespace DatabaseMcpServer.Models;

public sealed record DatabaseRuntimeStatus(string? CurrentDatabase, long RuntimeVersion, long SavedVersion, bool? HasPendingChanges);
