using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using MongoDB.Driver;
using System.Data.Common;
using System.Net.Sockets;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseConnectionProbe(DatabaseExecutionOptions options, ISqlSugarClientFactory sqlClients)
{
    internal static bool IsTransient(Exception exception) => exception is SocketException or TimeoutException
        or DbException { IsTransient: true } or TransientProbeException;

    private sealed class TransientProbeException(Exception inner) : Exception("数据库暂时无法连接。", inner);

    public async Task CheckAsync(DatabaseOperationContext context, CancellationToken cancellationToken = default)
    {
        DatabaseAccessPolicy.EnsureAllowed(context, DatabaseToolAccess.Read, options.Access);
        if (DatabaseProviderCatalog.Parse(context.DatabaseType) != SqlSugar.DbType.MongoDb)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var sql = sqlClients.CreateClient(new DatabaseOperationLease(context, () => { }));
            if (sql.Client.Ado.Connection is DbConnection connection)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            else
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "此 provider 不支持可取消连接测试。");
            return;
        }

        _ = MongoDocumentContract.ValidateUri(context.ConnectionString);
        var timeoutSeconds = Math.Min(options.DefaultCommandTimeoutSeconds, options.MaxCommandTimeoutSeconds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var sql = sqlClients.CreateClient(new DatabaseOperationLease(context, () => { }));
            // OpenAsync skips network I/O; use a bounded built-in query for a real connection check.
            await SqlCommandTimeout.WithTimeoutAsync(sql.Client, timeoutSeconds + 1, deadline.Token, async db =>
            {
                const string command = "aggregate _dbmcp_probe [{\"$limit\":1},{\"$project\":{\"_id\":1}}]";
                using var reader = await db.Ado.GetDataReaderAsync(command).ConfigureAwait(false);
                while (await QueryReader.ReadAsync(reader, deadline.Token).ConfigureAwait(false)) { }
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); throw; }
        catch (Exception ex) when (!deadline.IsCancellationRequested && ex is MongoConnectionException or TimeoutException)
        { throw new TransientProbeException(MongoError.Sanitize(ex)); }
        catch (Exception ex) { throw MongoError.Sanitize(ex, deadline.IsCancellationRequested); }
    }
}
