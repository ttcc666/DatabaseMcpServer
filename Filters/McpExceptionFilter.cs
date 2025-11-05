using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Filters;

/// <summary>
/// MCP 异常处理辅助类
/// </summary>
public static class McpExceptionFilter
{
    /// <summary>
    /// 处理异常并返回错误响应
    /// </summary>
    public static string HandleException(Exception ex, ILogger logger)
    {
        return ex switch
        {
            DatabaseMcpException dbEx => HandleDatabaseMcpException(dbEx, logger),
            SqlSugarException sqlEx => HandleSqlSugarException(sqlEx, logger),
            InvalidOperationException configEx => HandleConfigurationException(configEx, logger),
            _ => HandleUnknownException(ex, logger)
        };
    }

    private static string HandleDatabaseMcpException(DatabaseMcpException ex, ILogger logger)
    {
        logger.LogError(ex, "数据库MCP异常: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
        return CreateErrorResponse(ex.Message, ex.ErrorCode);
    }

    private static string HandleSqlSugarException(SqlSugarException ex, ILogger logger)
    {
        logger.LogError(ex, "数据库操作异常: {Message}", ex.Message);
        return CreateErrorResponse($"数据库操作失败: {ex.Message}", DatabaseErrorCode.QueryExecutionFailed);
    }

    private static string HandleConfigurationException(InvalidOperationException ex, ILogger logger)
    {
        logger.LogError(ex, "配置异常: {Message}", ex.Message);
        return CreateErrorResponse($"配置错误: {ex.Message}", DatabaseErrorCode.ConfigurationError);
    }

    private static string HandleUnknownException(Exception ex, ILogger logger)
    {
        logger.LogError(ex, "未知异常: {Message}", ex.Message);
        return CreateErrorResponse($"系统错误: {ex.Message}", DatabaseErrorCode.UnknownError);
    }

    /// <summary>
    /// 创建错误响应JSON
    /// </summary>
    private static string CreateErrorResponse(string message, DatabaseErrorCode errorCode)
    {
        var errorResult = ApiResult<object>.CreateError(message, errorCode);
        return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }
}