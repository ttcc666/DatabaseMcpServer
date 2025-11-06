using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using SqlSugar;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 数据库操作辅助类。
/// </summary>
internal class DatabaseHelper : IDatabaseHelperService
{
    private readonly ILogger<DatabaseHelper> _logger;

    public DatabaseHelper(ILogger<DatabaseHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 将字符串类型转换为 SqlSugar 的 DbType 枚举。
    /// </summary>
    /// <param name="dbType">数据库类型字符串 (MySql, SqlServer, Sqlite, PostgreSQL, Oracle)</param>
    /// <returns>对应的 DbType 枚举值</returns>
    /// <exception cref="ArgumentException">当数据库类型不支持时抛出</exception>
    public DbType ParseDbType(string dbType)
    {
        return dbType.ToLowerInvariant() switch
        {
            "mysql" => DbType.MySql,
            "sqlserver" => DbType.SqlServer,
            "sqlite" => DbType.Sqlite,
            "postgresql" => DbType.PostgreSQL,
            "oracle" => DbType.Oracle,
            _ => throw new ArgumentException($"不支持的数据库类型: {dbType}")
        };
    }

    /// <summary>
    /// 解析 JSON 格式的参数字符串为 SqlSugar 参数数组。
    /// </summary>
    /// <param name="parametersJson">JSON 格式的参数字符串</param>
    /// <returns>SqlSugar 参数数组,如果输入为空则返回 null</returns>
    public SugarParameter[]? ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        var paramsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);
        if (paramsDict == null)
            return null;

        return paramsDict.Select(kvp => new SugarParameter(kvp.Key, kvp.Value)).ToArray();
    }

    /// <summary>
    /// 将对象序列化为格式化的 JSON 字符串。
    /// </summary>
    /// <param name="data">要序列化的数据对象</param>
    /// <returns>格式化的 JSON 字符串</returns>
    public string SerializeResult(object data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// 创建 SqlSugar 数据库客户端实例。
    /// </summary>
    /// <param name="connectionString">数据库连接字符串</param>
    /// <param name="dbType">数据库类型</param>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public SqlSugarClient CreateClient(string connectionString, DbType dbType)
    {
        _logger.LogDebug("创建数据库客户端，类型: {DbType}", dbType);
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });

        // 配置 SQL 执行日志
        client.Aop.OnLogExecuting = (sql, pars) =>
        {
            var parameters = pars?.Length > 0 ?
                string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}")) :
                "无参数";
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", sql, parameters);
        };

        return client;
    }

    /// <summary>
    /// 检测 SQL 语句中是否包含危险操作。
    /// </summary>
    /// <param name="sql">要检测的 SQL 语句</param>
    /// <returns>如果包含危险操作返回 true,否则返回 false</returns>
    /// <remarks>
    /// 危险操作包括:
    /// - DROP TABLE (删除表)
    /// - DROP DATABASE (删除数据库)
    /// - TRUNCATE TABLE (截断表)
    /// - ALTER TABLE (修改表结构)
    /// - CREATE TABLE (创建表)
    /// </remarks>
    public bool DetectDangerousOperation(string sql)
    {
        var dangerousPatterns = new[]
        {
            @"\bDROP\s+TABLE\b",
            @"\bDROP\s+DATABASE\b",
            @"\bTRUNCATE\s+TABLE\b",
            @"\bALTER\s+TABLE\b",
            @"\bCREATE\s+TABLE\b"
        };

        return dangerousPatterns.Any(pattern =>
            Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase));
    }
}