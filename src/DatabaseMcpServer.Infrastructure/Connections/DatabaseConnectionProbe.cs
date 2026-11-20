using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseConnectionProbe(MongoClientRegistry clients, DatabaseExecutionOptions options, ISqlSugarClientFactory sqlClients)
{
    public async Task CheckAsync(DatabaseOperationContext context, CancellationToken cancellationToken = default)
    {
        DatabaseAccessPolicy.EnsureAllowed(context, DatabaseToolAccess.Read, options.Access);
        if (DatabaseProviderCatalog.Parse(context.DatabaseType) != SqlSugar.DbType.MongoDb)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var sql = sqlClients.CreateClient(new DatabaseOperationLease(context, () => { }));
            SqlCommandTimeout.WithTimeout(sql.Client, options.ResolveTimeout(null), db => { db.Ado.CheckConnection(); return true; });
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Min(options.DefaultCommandTimeoutSeconds, options.MaxCommandTimeoutSeconds)));
        try
        {
            using var lease = await clients.AcquireAsync(context, deadline.Token);
            await lease.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: deadline.Token);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); throw; }
        catch (Exception ex) { throw MongoError.Sanitize(ex, deadline.IsCancellationRequested); }
    }
}
