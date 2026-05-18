namespace DatabaseMcpServer.Web;

internal sealed record CliWebCommandOptions(
    string? ConfigPath,
    int? Port,
    bool OpenBrowser);

internal sealed record CliWebConfigContext(
    string ConfigPath,
    string Source)
{
    public bool ConfigExists => File.Exists(ConfigPath);
}

internal sealed record CliWebInitializeRequest(bool Force);

internal sealed record CliWebCreateFromPresetRequest(
    string DbType,
    string? Name,
    string? ConnectionString,
    string? Description,
    bool SetDefault,
    bool PrintOnly);

internal sealed record CliWebAddDatabaseRequest(
    string Name,
    string DbType,
    string ConnectionString,
    string? Description,
    bool SetDefault);

internal sealed record CliWebRenameDatabaseRequest(string NewName);

internal sealed record CliWebUpdateDatabaseRequest(
    string? DbType,
    string? ConnectionString,
    string? Description,
    bool ClearDescription,
    bool SetDefault,
    bool ApplyDbType,
    bool ApplyConnectionString,
    bool ApplyDescription,
    bool ApplyClearDescription,
    bool ApplySetDefault);

internal sealed record CliWebCloneDatabaseRequest(
    string NewName,
    bool SetDefault);

internal sealed record CliWebSwitchCurrentDatabaseRequest(string DatabaseName);

internal sealed record CliWebDoctorRequest(
    string? Name,
    bool TestConnections,
    bool FixSuggestions,
    bool SummaryOnly);
