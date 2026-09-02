using System;
using System.Threading;
using System.Threading.Tasks;
using DatabaseMcpServer.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace DatabaseMcpServer.Gui.Core.Services;

/// <summary>
/// 用 SqlSugar 直接连接数据库并执行 SELECT 1,不依赖 MCP 服务器进程。
/// </summary>
public static class DatabaseConnectionTester
{
    public static async Task<string?> TestAsync(
        string dbTypeString,
        string connectionString,
        int timeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dbTypeString))
        {
            return "数据库类型不能为空。";
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "连接字符串不能为空。";
        }

        SqlSugar.DbType parsedType;
        try
        {
            var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
            parsedType = helper.ParseDbType(dbTypeString);
        }
        catch (Exception ex)
        {
            return $"不支持的数据库类型: {dbTypeString}. {ex.Message}";
        }

        return await Task.Run(() =>
        {
            var config = new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = parsedType,
                IsAutoCloseConnection = true
            };

            using var client = new SqlSugarClient(config);
            try
            {
                client.Ado.CommandTimeOut = timeoutSeconds;
            }
            catch
            {
                // 部分驱动没有暴露 CommandTimeOut,忽略。
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (parsedType == SqlSugar.DbType.MongoDb)
                {
                    var raw = client.Ado.GetScalar("select 1")?.ToString();
                    return raw is null ? "无法连接到 MongoDB。" : null;
                }

                client.Ado.GetScalar("select 1");
                return null;
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}


