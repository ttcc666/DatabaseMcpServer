using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseCapabilities
{
    public static object Describe(DatabaseOperationContext context, DatabaseExecutionOptions options, ConnectionConfig settings)
    {
        var provider = settings.DbType;
        var product = DatabaseProviderCatalog.Parse(context.DatabaseType);
        var verified = DatabaseProviderCatalog.IsVerifiedProduct(product);
        var mongo = product == DbType.MongoDb;
        var bounded = DatabaseProviderCatalog.SupportsBoundedResults(provider);
        var more = settings.MoreSettings;
        var mapped = DatabaseOptimizationOptions.Mapped(product, more);
        var optimization = DatabaseOptimizationOptions.Describe(product, context.OptimizationSettings, more);
        return new
        {
            success = true, databaseName = context.DatabaseName, databaseType = context.DatabaseType, configurationVersion = context.ConfigurationVersion,
            actualProvider = provider.ToString(), productVerification = mongo ? "collection-integration-tested" : verified ? "integration-tested" : "unverified",
            accessMode = (DatabaseToolAccess)Math.Min((int)options.Access, (int)DatabaseAccessPolicy.Parse(context.AccessMode)) switch
                { DatabaseToolAccess.Read => "read-only", DatabaseToolAccess.Write => "read-write", _ => "schema-admin" },
            capabilities = new { sqlTools = product == DbType.MongoDb ? "unsupported" : "supported",
                collectionQueries = mongo ? "supported" : "unsupported", collectionWrites = mongo ? "supported" : "unsupported",
                aggregationPipelines = "unsupported", collectionTransactions = "unsupported",
                boundedMultipleResults = bounded ? "supported" : "unsupported",
                tablePagination = bounded ? "supported" : "unsupported", asyncQueryCancellation = verified || mongo ? "supported" : "unverified",
                typedProcedureOutput = product is DbType.MySql or DbType.SqlServer ? "supported" : "unsupported",
                nativeArrayParameters = "unsupported", refCursorParameters = "unsupported", synchronousCancellation = "unsupported",
                bulkCopyTool = "unsupported", schemaOperations = mongo ? "unsupported" : "provider-dependent" },
            capabilityVerification = new { boundedMultipleResults = bounded && verified ? "integration-tested" : "unverified",
                tablePagination = bounded && verified ? "integration-tested" : "unverified",
                collectionOperations = mongo ? "MongoDB 8.0 standalone CRUD; SRV/TLS/replica-set behavior unverified" : "not-applicable",
                asyncQueryCancellation = mongo ? "collection commands and cursor deadline; cancellation does not roll back writes"
                    : "query-path-only; synchronous procedures/GO/metadata/DDL do not support cancellation" },
            budgets = new { options.BoundedQueryDefaults, options.DefaultQueryRows, options.DefaultResponseBytes, options.MaxQueryRows,
                options.MaxResponseBytes, options.MaxCommandTimeoutSeconds, options.AllowInfiniteTimeout },
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
                var b = new MongoDB.Driver.MongoUrlBuilder(connectionString);
                return new { status = "validated-offline", databaseSpecified = !string.IsNullOrWhiteSpace(b.DatabaseName),
                    maxPoolSize = b.MaxConnectionPoolSize, tls = b.UseTls, retryWrites = b.RetryWrites,
                    acknowledgedWrites = new MongoDB.Driver.WriteConcern(b.W).IsAcknowledged,
                    nativeDriver = "MongoDB.Driver", documentFormat = "Canonical Extended JSON v2" };
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
                    sources = Sources(b, "Pooling", "Maximum Pool Size", "SSL Mode", "Command Timeout") };
            }
            if (provider == DbType.SqlServer)
            {
                var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
                return new { status = "validated", pooling = b.Pooling, maxPoolSize = b.MaxPoolSize, encrypt = b.Encrypt.ToString(), trustServerCertificate = b.TrustServerCertificate,
                    sources = Sources(b, "Pooling", "Max Pool Size", "Encrypt", "Trust Server Certificate") };
            }
            return new { status = "unverified" };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return new { status = "invalid", message = "驱动连接选项无效；请检查配置。连接细节已隐藏。" };
        }
    }

    private static Dictionary<string, string> Sources(System.Data.Common.DbConnectionStringBuilder builder, params string[] keys)
        => keys.ToDictionary(key => key, key => builder.ShouldSerialize(key) ? "connection-string" : "driver-default");
}
