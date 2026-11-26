namespace DatabaseMcpServer.Models;

public sealed class AtomicBatchResult
{
    public bool Success { get; set; }
    public bool Atomic { get; set; } = true;
    public bool Cancelled { get; set; }
    public string TransactionOutcome { get; set; } = "not-started";
    public int TotalCommands { get; set; }
    public long CommittedAffectedRows { get; set; }
    public List<AtomicCommandResult> Results { get; set; } = [];
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? Retryable { get; set; }
    public string? Phase { get; set; }
    public string? Capability { get; set; }
    public string? Hint { get; set; }
}

public sealed class AtomicCommandResult
{
    public int CommandIndex { get; set; }
    public bool Executed { get; set; }
    public string Outcome { get; set; } = "not-executed";
    public int? AffectedRows { get; set; }
}
