using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Prompts;

[McpServerPromptType]
internal sealed class DatabasePrompts
{
    [McpServerPrompt(Name = "analyze_slow_query", Title = "Analyze Slow Query")]
    [Description("Step-by-step read-only SQL performance investigation using capabilities, table schema, and explain_query.")]
    public static ChatMessage AnalyzeSlowQuery(
        [Description("SQL SELECT or WITH query to analyze")] string sql,
        [Description("Optional target connection name")] string? databaseName = null)
    {
        var target = string.IsNullOrWhiteSpace(databaseName) ? "the active connection" : $"connection '{databaseName}'";
        return new(ChatRole.User, $"""
            Inspect the performance of the following read-only SQL on {target}:
            ```sql
            {sql}
            ```
            Workflow:
            1. Call `get_database_capabilities` to confirm provider capabilities and timeouts.
            2. Inspect referenced tables using `get_table_schema` (columns, primaryKeys, indexes).
            3. Run `explain_query` (if supported) to inspect the estimated execution plan without running expensive scans.
            4. Recommend index or query improvements; if running bounded verification queries, always bind `@param` values and inspect `limitReached`/`truncated`.
            """);
    }

    [McpServerPrompt(Name = "safe_schema_migration", Title = "Safe Schema Migration")]
    [Description("Guided checklist before applying schema changes, backups, or atomic DML batches.")]
    public static ChatMessage SafeSchemaMigration(
        [Description("Target table name")] string tableName,
        [Description("Intended schema or data change")] string changeGoal,
        [Description("Optional target connection name")] string? databaseName = null)
    {
        var target = string.IsNullOrWhiteSpace(databaseName) ? "the active connection" : $"connection '{databaseName}'";
        return new(ChatRole.User, $"""
            Plan and validate a safe migration on {target} for table `{tableName}`:
            Goal: {changeGoal}
            Workflow:
            1. Call `get_database_capabilities` to check `accessMode`, `schemaOperations`, and `atomicDmlBatch`.
            2. Call `get_table_schema` (including `foreignKeys`, `constraints`, `indexes` where supported) and `list_objects` for dependent triggers/views.
            3. Prefer explicit schema tools (`create_table`, `add_column`, `create_index`, `drop_index`) or `batch_execute_commands(atomic=true)` where supported.
            4. Check `transactionOutcome` / `writeOutcome` before retrying on timeout or cancellation.
            """);
    }
}
