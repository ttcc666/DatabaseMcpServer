namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库错误码枚举
/// </summary>
public enum DatabaseErrorCode
{
    /// <summary>
    /// 连接失败
    /// </summary>
    ConnectionFailed = 1001,

    /// <summary>
    /// 查询执行失败
    /// </summary>
    QueryExecutionFailed = 1002,

    /// <summary>
    /// 危险操作
    /// </summary>
    DangerousOperation = 1003,

    /// <summary>
    /// 无效参数
    /// </summary>
    InvalidParameters = 1004,

    /// <summary>
    /// 配置错误
    /// </summary>
    ConfigurationError = 1005,

    /// <summary>
    /// 未知错误
    /// </summary>
    UnknownError = 9999
}

/// <summary>
/// 统一返回结果包装器
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class ApiResult<T>
{
    /// <summary>
    /// 操作是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 返回数据
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 错误码
    /// </summary>
    public DatabaseErrorCode? ErrorCode { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static ApiResult<T> CreateSuccess(T data)
    {
        return new ApiResult<T>
        {
            Success = true,
            Data = data
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static ApiResult<T> CreateError(string errorMessage, DatabaseErrorCode errorCode)
    {
        return new ApiResult<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }
}

/// <summary>
/// 数据库异常类
/// </summary>
public class DatabaseMcpException : Exception
{
    public DatabaseErrorCode ErrorCode { get; }

    public DatabaseMcpException(DatabaseErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public DatabaseMcpException(DatabaseErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}