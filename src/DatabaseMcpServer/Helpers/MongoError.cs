using DatabaseMcpServer.Models;
using MongoDB.Driver;

namespace DatabaseMcpServer.Helpers;

internal static class MongoError
{
    public static DatabaseMcpException Sanitize(Exception exception, bool deadlineExceeded = false) => exception switch
    {
        DatabaseMcpException known => known,
        _ when deadlineExceeded => new(DatabaseErrorCode.CommandTimeout, "Mongo 操作超时；已提交的写入可能生效，请先核实结果再决定是否重试。"),
        MongoExecutionTimeoutException or OperationCanceledException =>
            new(DatabaseErrorCode.CommandTimeout, "Mongo 操作超时；已提交的写入可能生效，请先核实结果再决定是否重试。"),
        MongoConnectionException or MongoAuthenticationException or TimeoutException =>
            new(DatabaseErrorCode.ConnectionFailed, "Mongo 连接或认证失败；连接细节已隐藏。"),
        MongoCommandException { Code: 13 or 18 } =>
            new(DatabaseErrorCode.ConnectionFailed, "Mongo 认证或数据库授权失败。"),
        MongoException => new(DatabaseErrorCode.QueryExecutionFailed, "Mongo 操作失败；服务器错误细节及文档值已隐藏。"),
        _ => new(DatabaseErrorCode.QueryExecutionFailed, "Mongo 操作失败；请检查配置和输入，错误细节已隐藏。")
    };
}
