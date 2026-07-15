namespace DatabaseMcpServer.Cli;

internal static class CliWriteProtection
{
    private static readonly HashSet<string> ProtectedToolNames = new(StringComparer.Ordinal)
    {
        "execute_command",
        "call_stored_procedure",
        "call_stored_procedure_with_output",
        "execute_command_with_go",
        "batch_execute_commands",
        "create_table",
        "drop_table",
        "truncate_table",
        "backup_table",
        "rename_table",
        "add_column",
        "update_column",
        "drop_column",
        "rename_column",
        "add_primary_key",
        "drop_constraint",
        "create_index",
        "add_default_value",
        "add_table_remark",
        "delete_table_remark",
        "add_column_remark",
        "delete_column_remark",
        "drop_view",
        "drop_func",
        "drop_proc"
    };

    public static bool RequiresConfirmation(string toolName)
    {
        return ProtectedToolNames.Contains(toolName);
    }
}
