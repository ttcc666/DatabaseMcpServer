namespace DatabaseMcpServer.Tests;

internal static class ToolContract
{
    public static string[] SqliteUnsupported => ["get_data_base_list", "is_any_constraint", "rename_column", "add_primary_key", "drop_constraint",
        "add_default_value", "add_table_remark", "is_any_table_remark", "delete_table_remark", "add_column_remark", "delete_column_remark"];
    public static string[] SqliteMongoFull => Names.Except(new[] { "call_stored_procedure", "execute_command_with_go", "drop_func", "drop_proc" }
        .Concat(SqliteUnsupported)).Order().ToArray();
    public static string[] RemovedNames => """
        test_connection_by_name test_connection_with_retry get_view_info_list get_table_info_list
        get_column_infos_by_table_name get_is_identities get_primaries get_index_list get_proc_list
        get_func_list get_trigger_names sql_query_single get_scalar bulk_insert
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string[] MongoFull => Names.Where(n => n.StartsWith("mongo_", StringComparison.Ordinal) || new[]
        { "get_database_capabilities", "test_connection", "get_database_config", "validate_configuration", "reload_database_config",
          "list_databases", "switch_database", "get_current_database", "health_check" }.Contains(n)).ToArray();

    // Runtime catalogs filter these tools by configured providers and access.
    public static string[] Names => """
        test_connection list_databases switch_database get_current_database health_check
        get_database_config validate_configuration reload_database_config get_data_base_list
        is_any_table is_any_column is_any_constraint create_table drop_table truncate_table backup_table rename_table
        add_column update_column drop_column rename_column add_primary_key drop_constraint create_index drop_index
        add_default_value add_table_remark is_any_table_remark delete_table_remark add_column_remark delete_column_remark
        drop_view drop_func drop_proc get_table_schema query_database
        get_data_set_all query_table_page batch_sql_query execute_command insert_row call_stored_procedure
        get_database_capabilities execute_command_with_go batch_execute_commands mongo_find
        mongo_aggregate mongo_insert_many mongo_update mongo_delete mongo_truncate
        list_objects get_object_definition explain_query
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    public static string[] SqlAnalysis => new[] { "list_databases", "get_database_capabilities", "test_connection", "list_objects",
        "get_table_schema", "get_object_definition", "query_database", "query_table_page", "explain_query" }.Order(StringComparer.Ordinal).ToArray();
    public static string[] MongoAnalysis => new[] { "list_databases", "get_database_capabilities", "test_connection",
        "mongo_find", "mongo_aggregate" }.Order(StringComparer.Ordinal).ToArray();
}
