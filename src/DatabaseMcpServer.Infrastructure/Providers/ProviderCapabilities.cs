using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class ProviderCapabilities
{
    private sealed record Rule(DbType[] Providers, bool Exclude = false, bool SchemaOperation = false);

    private static readonly IReadOnlyDictionary<DatabaseCapability, Rule> Rules = new Dictionary<DatabaseCapability, Rule>
    {
        [DatabaseCapability.MongoCollections] = new([DbType.MongoDb]),
        [DatabaseCapability.BoundedResults] = new([DbType.Sqlite, DbType.MySql, DbType.PostgreSQL, DbType.SqlServer]),
        [DatabaseCapability.NativeMetadata] = new([DbType.Sqlite, DbType.MySql, DbType.PostgreSQL, DbType.SqlServer]),
        [DatabaseCapability.AsyncSchema] = new([DbType.Sqlite, DbType.MySql, DbType.PostgreSQL, DbType.SqlServer, DbType.DuckDB]),
        [DatabaseCapability.EstimatedPlan] = new([DbType.Sqlite, DbType.MySql, DbType.PostgreSQL, DbType.SqlServer]),
        [DatabaseCapability.AtomicDml] = new([DbType.Sqlite, DbType.PostgreSQL, DbType.SqlServer]),
        [DatabaseCapability.StoredProcedures] = new([DbType.MySql, DbType.SqlServer]),
        [DatabaseCapability.InsertIdentity] = new([DbType.Sqlite, DbType.MySql, DbType.SqlServer]),
        [DatabaseCapability.SqlServerBatches] = new([DbType.SqlServer]),
        [DatabaseCapability.Sql] = new([DbType.MongoDb], Exclude: true),
        [DatabaseCapability.Routines] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.ClickHouse, DbType.TDengine, DbType.DuckDB, DbType.Doris], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.DatabaseListing] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.Oracle, DbType.Dm, DbType.OceanBaseForOracle, DbType.GBase], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.ConstraintLookup] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.ClickHouse, DbType.TDengine, DbType.DuckDB, DbType.Doris], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.RenameColumn] = new([DbType.Sqlite, DbType.MongoDb, DbType.ClickHouse, DbType.TDengine, DbType.GBase], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.RenameTable] = new([DbType.MongoDb, DbType.ClickHouse, DbType.Oscar, DbType.QuestDB, DbType.GBase, DbType.TDengine], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.AddPrimaryKey] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.ClickHouse, DbType.TDengine, DbType.Doris], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.DropConstraint] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.ClickHouse, DbType.TDengine, DbType.DuckDB, DbType.Doris], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.DropIndex] = new([DbType.MongoDb, DbType.TDengine, DbType.QuestDB, DbType.ClickHouse], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.DefaultValue] = new([DbType.Sqlite, DbType.MongoDb], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.SchemaComments] = new([DbType.Sqlite, DbType.MongoDb, DbType.QuestDB, DbType.TDengine, DbType.Doris], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.TableRemarkLookup] = new([DbType.Sqlite, DbType.MongoDb, DbType.MySql, DbType.Tidb, DbType.OceanBase, DbType.PolarDB, DbType.GoldenDB, DbType.Doris, DbType.TDSQL, DbType.PostgreSQL, DbType.Vastbase, DbType.GaussDB, DbType.OpenGauss, DbType.GaussDBNative, DbType.Kdbndp, DbType.Oscar, DbType.HG, DbType.DuckDB, DbType.ClickHouse, DbType.TDengine, DbType.QuestDB, DbType.GBase], Exclude: true, SchemaOperation: true),
        [DatabaseCapability.DeleteColumnRemark] = new([DbType.Sqlite, DbType.MongoDb, DbType.MySql, DbType.Tidb, DbType.OceanBase, DbType.PolarDB, DbType.GoldenDB, DbType.Doris, DbType.TDSQL, DbType.ClickHouse, DbType.TDengine, DbType.QuestDB], Exclude: true, SchemaOperation: true)
    };

    public static bool Supports(DbType provider, DatabaseCapability capability)
        => Rules.TryGetValue(capability, out var rule) && (rule.Exclude != rule.Providers.Contains(provider));

    public static string[] SupportedProviders(DatabaseCapability? capability) => capability.HasValue && Rules.TryGetValue(capability.Value, out var rule) && !rule.Exclude
        ? rule.Providers.Select(p => p.ToString()).ToArray() : [];

    public static string[] UnsupportedProviders(DatabaseCapability? capability) => capability.HasValue && Rules.TryGetValue(capability.Value, out var rule) && rule.Exclude
        ? rule.Providers.Select(p => p.ToString()).ToArray() : [];

    public static IReadOnlyList<SchemaSectionKind> DefaultSchemaSections { get; } = Array.AsReadOnly(new[]
        { SchemaSectionKind.Columns, SchemaSectionKind.PrimaryKeys, SchemaSectionKind.IdentityColumns, SchemaSectionKind.Indexes });

    public static CapabilityStatus SectionStatus(DbType provider, SchemaSectionKind section) => section switch
    {
        SchemaSectionKind.Columns or SchemaSectionKind.PrimaryKeys or SchemaSectionKind.IdentityColumns or SchemaSectionKind.Indexes
            => Supports(provider, DatabaseCapability.AsyncSchema) ? CapabilityStatus.Supported
                : Supports(provider, DatabaseCapability.Sql) ? CapabilityStatus.ProviderDependent : CapabilityStatus.Unsupported,
        SchemaSectionKind.Constraints when provider == DbType.Sqlite => CapabilityStatus.Partial,
        SchemaSectionKind.Comments when provider == DbType.Sqlite => CapabilityStatus.Unsupported,
        SchemaSectionKind.ForeignKeys or SchemaSectionKind.Constraints or SchemaSectionKind.Comments
            => Supports(provider, DatabaseCapability.NativeMetadata) ? CapabilityStatus.Supported : CapabilityStatus.Unsupported,
        _ => CapabilityStatus.Unsupported
    };

    public static Dictionary<string, Dictionary<string, string>> Details(DbType provider) => new()
    {
        ["schemaSections"] = Enum.GetValues<SchemaSectionKind>().ToDictionary(CapabilityNames<SchemaSectionKind>.Name, s => SectionStatus(provider, s).Name()),
        ["schemaOperations"] = Rules.Where(r => r.Value.SchemaOperation).Select(r => r.Key)
            .ToDictionary(c => c.ToString(), c => (Supports(provider, c) ? CapabilityStatus.ProviderDependent : CapabilityStatus.Unsupported).Name()),
        ["cliExport"] = new[] { "jsonl", "csv" }.ToDictionary(f => f, _ => !Supports(provider, DatabaseCapability.Sql)
            ? CapabilityStatus.Unsupported.Name() : DatabaseProviderCatalog.IsVerifiedProduct(provider) ? CapabilityStatus.Supported.Name() : CapabilityStatus.ProviderDependentUnverified.Name())
    };

    public static object DescribeCatalog() => new
    {
        schemaSections = CapabilityNames<SchemaSectionKind>.All,
        defaultSchemaSections = DefaultSchemaSections.Select(CapabilityNames<SchemaSectionKind>.Name),
        capabilities = Enum.GetValues<DatabaseCapability>().Select(c => new
        {
            name = c.ToString(), supportedProviders = SupportedProviders(c), unsupportedProviders = UnsupportedProviders(c)
        }),
        providers = Rules.Values.SelectMany(r => r.Providers).Append(DbType.Oracle).Distinct().OrderBy(p => p.ToString(), StringComparer.Ordinal)
            .Select(p => new { name = p.ToString(), details = Details(p) })
    };

    public static void Require(DbType provider, DatabaseCapability capability)
    {
        if (!Supports(provider, capability))
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, $"{provider} 不支持 {capability}。")
            { Phase = "capability", Capability = capability.ToString() };
    }
}
