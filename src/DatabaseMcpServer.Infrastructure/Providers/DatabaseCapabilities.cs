using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseCapabilities
{
    public static object Describe(DatabaseOperationContext context, DatabaseExecutionOptions options, ConnectionConfig settings)
    {
        var provider = DatabaseProviderCatalog.DriverFamily(settings.DbType);
        var product = DatabaseProviderCatalog.Parse(context.DatabaseType);
        var verified = DatabaseProviderCatalog.IsVerifiedProduct(product);
        var mongo = product == DbType.MongoDb;
        var bounded = DatabaseProviderCatalog.SupportsBoundedResults(product);
        var more = settings.MoreSettings;
        var mapped = DatabaseOptimizationOptions.Mapped(product, more);
        var optimization = DatabaseOptimizationOptions.Describe(product, context.OptimizationSettings, more);
        return new
        {
            success = true, databaseName = context.DatabaseName, databaseType = context.DatabaseType, configurationVersion = context.ConfigurationVersion,
            actualProvider = provider.ToString(), productVerification = mongo ? "sqlsugar-ado-integrated" : verified ? "integration-tested" : "unverified",
            accessMode = (DatabaseToolAccess)Math.Min((int)options.Access, (int)DatabaseAccessPolicy.Parse(context.AccessMode)) switch
                { DatabaseToolAccess.Read => "read-only", DatabaseToolAccess.Write => "read-write", _ => "schema-admin" },
            capabilities = new { sqlTools = product == DbType.MongoDb ? "unsupported" : "supported",
                collectionQueries = mongo ? "supported-find-aggregate" : "unsupported", collectionWrites = mongo ? "supported-batch-dml-and-truncate" : "unsupported",
                aggregationPipelines = mongo ? "supported-read-only-allowlist" : "unsupported", collectionTransactions = "unsupported",
                collectionInfo = "unsupported",
                collectionExplain = "unsupported",
                objectMetadata = NativeObjectCatalog.Supports(product) ? "supported" : "unsupported",
                objectListing = mongo ? "unsupported" : NativeObjectCatalog.Supports(product) ? "native-keyset-paging" : "provider-dependent-client-paging",
                estimatedQueryPlan = DatabaseProviderCatalog.SupportsEstimatedPlan(product) ? "supported" : "unsupported",
                planParameterMode = product == DbType.SqlServer ? "scalar-types-only; no parameter value sniffing" : "provider-dependent",
                atomicDmlBatch = DatabaseProviderCatalog.SupportsAtomicDml(product) ? "supported-insert-update-delete" : "unsupported",
                boundedMultipleResults = bounded ? "supported" : "unsupported",
                tablePagination = bounded ? "supported" : "unsupported", asyncQueryCancellation = mongo ? "provider-limited" : verified ? "supported" : "unverified",
                typedProcedureOutput = ProviderCapabilities.Supports(product, DatabaseCapability.StoredProcedures) ? "supported" : "unsupported",
                keysetPagination = bounded ? "composite-primary-key" : "unsupported",
                batchSchema = ProviderCapabilities.Supports(product, DatabaseCapability.AsyncSchema)
                    ? "shared-budgets-per-table-status" : mongo ? "unsupported" : "provider-dependent",
                nativeArrayParameters = "unsupported", refCursorParameters = "unsupported", synchronousCancellation = "unsupported",
                bulkCopyTool = "unsupported",
                insertRow = ProviderCapabilities.Supports(product, DatabaseCapability.InsertIdentity) ? "single-row-integer-identity" : "unsupported",
                schemaOperations = mongo ? "unsupported" : "provider-dependent" },
            capabilityDetails = ProviderCapabilities.Details(product),
            fieldReading = new
            {
                text = provider == DbType.Sqlite ? "native-byte-preflight" : provider is DbType.PostgreSQL or DbType.SqlServer ? "async-budgeted-chunks" : "driver-materialized",
                binary = provider == DbType.Sqlite ? "native-byte-preflight" : provider is DbType.PostgreSQL or DbType.SqlServer ? "async-budgeted-stream" : "length-preflight-when-driver-supports-GetBytes",
                readerMode = provider is DbType.PostgreSQL or DbType.SqlServer ? "sequential-for-bounded-text-commands" : "driver-default",
                fallback = "transactions, replicas and custom middleware retain SqlSugar reader behavior; SQL Server procedure/explain use native sequential readers",
                scope = "application-field-allocation; sequential mode reduces supported driver buffering, but database memory and all native allocations are outside the budget"
            },
            capabilityVerification = new { boundedMultipleResults = bounded && verified ? "integration-tested" : "unverified",
                tablePagination = bounded && verified ? "integration-tested" : "unverified",
                collectionOperations = mongo ? "SqlSugar.MongoDbCore built-in ADO commands; provider materializes results before response byte checks" : "not-applicable",
                asyncQueryCancellation = mongo ? "Ado.CancellationToken forwarded; initial aggregate is synchronous and cannot be interrupted by the request token; cleanup is awaited; cancellation does not roll back writes"
                    : "queries/procedures/native-metadata support async cancellation; synchronous GO/DDL do not" },
            budgets = new { options.BoundedQueryDefaults, options.DefaultQueryRows, options.DefaultResponseBytes, options.MaxQueryRows,
                options.MaxResponseBytes, options.DefaultCommandTimeoutSeconds, options.MaxCommandTimeoutSeconds, options.AllowInfiniteTimeout,
                options.MaxConcurrent, options.MaxQueued, options.MaxTotalConcurrent, options.MaxTotalQueued, options.QueueTimeoutSeconds,
                admissionScope = "shared-application-coordinator; independent processes and hosts have separate limits" },
            asyncSqlTimeoutScope = "queue-execution-reading; cleanup awaited before lease release; atomic batches use totalTimeoutSeconds",
            metadataCache = new { ttlSeconds = options.MetadataCacheSeconds, maxEntries = options.MetadataCacheEntries },
            optimizationSettings = optimization, sqlSugarSettings = mapped,
            connectionOptions = DriverOptions(provider, settings.ConnectionString),
            connectionOptionPriority = "explicit-connection-string-then-driver-default; optimizationSettings do not override driver options",
            responseBudgetScope = "tool-json-utf8; MCP text and structuredContent duplicate payload; not a process-memory limit"
        };
    }

    internal static object DriverOptions(DbType provider, string connectionString)
    {
        try
        {
            if (provider == DbType.MongoDb)
            {
                var b = MongoDocumentContract.ValidateUri(connectionString);
                return new { status = "validated-offline", databaseSpecified = !string.IsNullOrWhiteSpace(b.DatabaseName),
                    maxPoolSize = b.MaxConnectionPoolSize, tls = b.UseTls, retryWrites = b.RetryWrites,
                    acknowledgedWrites = new MongoDB.Driver.WriteConcern(b.W).IsAcknowledged,
                    nativeDriver = "SqlSugar.MongoDbCore", documentFormat = "input: Extended JSON v2; output: provider-converted JSON, nested BSON uses Extended JSON" };
            }
            if (provider == DbType.MySql)
            {
                var b = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);
                return new { status = "validated", pooling = b.Pooling, maxPoolSize = b.MaximumPoolSize, sslMode = b.SslMode.ToString(),
                    allowUserVariables = b.AllowUserVariables, allowLoadLocalInfile = b.AllowLoadLocalInfile,
                    sources = Sources(b, "Pooling", "Maximum Pool Size", "SSL Mode", "Allow User Variables", "Allow Load Local Infile") };
            }
            if (provider == DbType.PostgreSQL)
            {
                var b = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                return new { status = "validated", pooling = b.Pooling, maxPoolSize = b.MaxPoolSize, sslMode = b.SslMode.ToString(), commandTimeout = b.CommandTimeout,
                    cancellationTimeoutMs = b.CancellationTimeout,
                    sources = Sources(b, "Pooling", "Maximum Pool Size", "SSL Mode", "Command Timeout", "Cancellation Timeout") };
            }
            if (provider == DbType.SqlServer)
            {
                var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
                return new { status = "validated", pooling = b.Pooling, maxPoolSize = b.MaxPoolSize, encrypt = b.Encrypt.ToString(), trustServerCertificate = b.TrustServerCertificate,
                    sources = Sources(b, "Pooling", "Max Pool Size", "Encrypt", "Trust Server Certificate") };
            }
            return new { status = "unverified" };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or DatabaseMcpException)
        {
            return new { status = "invalid", message = "驱动连接选项无效；请检查配置。连接细节已隐藏。" };
        }
    }

    private static Dictionary<string, string> Sources(System.Data.Common.DbConnectionStringBuilder builder, params string[] keys)
        => keys.ToDictionary(key => key, key => builder.ShouldSerialize(key) ? "connection-string" : "driver-default");
}
