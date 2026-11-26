using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data.Common;

namespace DatabaseMcpServer.Services;

/// <summary>
/// Maps database failures to transport-independent application errors.
/// </summary>
internal static class DatabaseErrorMapper
{
    /// <summary>
    /// 处理异常并返回错误响应
    /// </summary>
    public static ApiResult<object> Map(Exception ex, ILogger logger)
    {
        if (ClassifyProvider(ex) is { } classified)
        {
            logger.LogWarning("数据库操作失败: {ErrorCode}", classified);
            return CreateErrorResponse(classified == DatabaseErrorCode.CommandTimeout ? "数据库命令超时。" : "数据库连接失败，请检查服务可达性和连接配置。", classified);
        }
        return ex switch
        {
            DatabaseMcpException dbEx => HandleDatabaseMcpException(dbEx, logger),
            SqlSugarException sqlEx => HandleSqlSugarException(sqlEx, logger),
            DbException dbEx => HandleProviderException(dbEx, logger),
            System.Text.Json.JsonException => CreateErrorResponse("参数必须是有效且符合要求的 JSON。", DatabaseErrorCode.InvalidParameters),
            TimeoutException => CreateErrorResponse("数据库操作超时。", DatabaseErrorCode.CommandTimeout),
            NotSupportedException => CreateErrorResponse("当前数据库或驱动不支持此操作。", DatabaseErrorCode.UnsupportedCapability),
            InvalidOperationException configEx => HandleConfigurationException(configEx, logger),
            _ => HandleUnknownException(ex, logger)
        };
    }

    internal static DatabaseErrorCode? ClassifyProvider(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is TimeoutException) return DatabaseErrorCode.CommandTimeout;
            if (current is MySqlConnector.MySqlException mysql)
            {
                if (mysql.Number is -1 or 3024) return DatabaseErrorCode.CommandTimeout;
                if (mysql.Number is 1042 or 1045 or 1049 or 2002 or 2003) return DatabaseErrorCode.ConnectionFailed;
            }
            // Older drivers hide DbException.SqlState or expose the state as Code.
            var sqlState = current switch
            {
                Npgsql.PostgresException pg => pg.SqlState,
                Kdbndp.KingbaseException kingbase => kingbase.SqlState,
                OpenGauss.NET.PostgresException gauss => gauss.SqlState,
                Nhgdb.HighgoException highgo => highgo.SqlState,
                System.Data.OscarClient.OscarException oscar => oscar.Code,
                _ => null
            };
            if (sqlState != null)
            {
                if (sqlState == "57014") return DatabaseErrorCode.CommandTimeout;
                if (sqlState.StartsWith("08", StringComparison.Ordinal) || sqlState.StartsWith("28", StringComparison.Ordinal) || sqlState == "3D000")
                    return DatabaseErrorCode.ConnectionFailed;
            }
            if (current is Microsoft.Data.SqlClient.SqlException sql)
            {
                if (sql.Number is -2 or 1222) return DatabaseErrorCode.CommandTimeout;
                if (sql.Number is 53 or 64 or 233 or 4060 or 18456 or 10060 or 10061) return DatabaseErrorCode.ConnectionFailed;
            }
            if (current is Oracle.ManagedDataAccess.Client.OracleException oracle)
            {
                if (oracle.Number == 1013) return DatabaseErrorCode.CommandTimeout;
                if (oracle.Number is 1017 or 3113 or 3114 or 12154 or 12170 or 12514 or 12541) return DatabaseErrorCode.ConnectionFailed;
            }
            if (current is System.Net.Sockets.SocketException) return DatabaseErrorCode.ConnectionFailed;
        }
        return null;
    }

    private static ApiResult<object> HandleProviderException(DbException ex, ILogger logger)
    {
        logger.LogError(ex, "数据库驱动异常: {Message}", ex.Message);
        return CreateErrorResponse($"数据库操作失败: {ex.Message}", DatabaseErrorCode.QueryExecutionFailed);
    }

    private static ApiResult<object> HandleDatabaseMcpException(DatabaseMcpException ex, ILogger logger)
    {
        logger.LogError(ex, "数据库MCP异常: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
        var result = CreateErrorResponse(ex.Message, ex.ErrorCode);
        result.Phase = ex.Phase ?? result.Phase;
        result.Capability = ex.Capability;
        return result;
    }

    private static ApiResult<object> HandleSqlSugarException(SqlSugarException ex, ILogger logger)
    {
        logger.LogError(ex, "数据库操作异常: {Message}", ex.Message);
        return CreateErrorResponse($"数据库操作失败: {ex.Message}", DatabaseErrorCode.QueryExecutionFailed);
    }

    private static ApiResult<object> HandleConfigurationException(InvalidOperationException ex, ILogger logger)
    {
        logger.LogError(ex, "配置异常: {Message}", ex.Message);
        return CreateErrorResponse($"配置错误: {ex.Message}", DatabaseErrorCode.ConfigurationError);
    }

    private static ApiResult<object> HandleUnknownException(Exception ex, ILogger logger)
    {
        logger.LogError(ex, "未知异常: {Message}", ex.Message);
        return CreateErrorResponse($"系统错误: {ex.Message}", DatabaseErrorCode.UnknownError);
    }

    /// <summary>
    /// 创建错误响应JSON
    /// </summary>
    private static ApiResult<object> CreateErrorResponse(string message, DatabaseErrorCode errorCode)
    {
        var errorResult = ApiResult<object>.CreateError(message, errorCode);
        return errorResult;
    }
}
