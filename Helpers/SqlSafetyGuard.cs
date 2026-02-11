using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using SqlSugar;
using System.Text;
using System.Text.RegularExpressions;

namespace DatabaseMcpServer.Helpers;

internal static class SqlSafetyGuard
{
    private static readonly HashSet<string> AllowedReadOnlyLeadingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "WITH",
        "SHOW",
        "DESC",
        "DESCRIBE",
        "EXPLAIN",
        "PRAGMA"
    };

    private static readonly Regex LeadingTokenRegex = new(@"^[A-Za-z]+", RegexOptions.Compiled);

    private static readonly Regex ForbiddenWriteKeywordRegex = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|REPLACE|UPSERT|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnsafeWhereClauseRegex = new(
        @"(--|/\*|\*/|;|\b(INSERT|UPDATE|DELETE|MERGE|REPLACE|UPSERT|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ValidIdentifierRegex = new(@"^[A-Za-z0-9_.$]+$", RegexOptions.Compiled);

    public static void EnsureReadOnlySql(string sql, IDatabaseHelperService databaseHelper, bool allowMultipleStatements = false)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "SQL 不能为空");
        }

        if (databaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到潜在危险操作，只允许只读 SQL。");
        }

        var statements = SplitStatements(sql)
            .Select(NormalizeSql)
            .Where(statement => statement.Length > 0)
            .ToList();

        if (statements.Count == 0)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "SQL 不能为空");
        }

        if (!allowMultipleStatements && statements.Count > 1)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "只允许执行单条只读 SQL，不支持多语句。");
        }

        foreach (var statement in statements)
        {
            ValidateSingleReadOnlyStatement(statement);
        }
    }

    public static void EnsureSafeWhereClause(string? whereClause)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
        {
            return;
        }

        var normalized = whereClause.Trim();
        if (normalized.Length > 2000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "whereClause 过长，请改用参数化查询。");
        }

        if (UnsafeWhereClauseRegex.IsMatch(normalized))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "whereClause 包含不安全片段，仅允许纯过滤表达式。");
        }
    }

    public static void EnsureSafeTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "表名不能为空");
        }

        if (!ValidIdentifierRegex.IsMatch(tableName))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "表名仅允许字母、数字、下划线、点号和美元符号。");
        }
    }

    public static string QuoteTableName(string tableName, DbType dbType)
    {
        var segments = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var quotedSegments = segments.Select(segment => QuoteIdentifierSegment(segment, dbType));
        return string.Join('.', quotedSegments);
    }

    private static void ValidateSingleReadOnlyStatement(string statement)
    {
        var firstTokenMatch = LeadingTokenRegex.Match(statement);
        var firstToken = firstTokenMatch.Success ? firstTokenMatch.Value : string.Empty;
        if (!AllowedReadOnlyLeadingKeywords.Contains(firstToken))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "只读工具仅支持 SELECT/WITH/SHOW/DESC/EXPLAIN/PRAGMA。");
        }

        var sqlWithoutStringLiterals = StripStringLiterals(statement);
        if (ForbiddenWriteKeywordRegex.IsMatch(sqlWithoutStringLiterals))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "只读工具检测到写操作关键字，已拒绝执行。");
        }
    }

    private static string QuoteIdentifierSegment(string segment, DbType dbType)
    {
        return dbType switch
        {
            DbType.SqlServer => $"[{segment.Replace("]", "]]", StringComparison.Ordinal)}]",
            DbType.MySql or DbType.MySqlConnector or DbType.ClickHouse or DbType.Tidb or DbType.Doris or DbType.OceanBase =>
                $"`{segment.Replace("`", "``", StringComparison.Ordinal)}`",
            _ => $"\"{segment.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        };
    }

    private static string NormalizeSql(string sql)
    {
        var strippedLeadingComments = StripLeadingComments(sql).Trim();
        if (strippedLeadingComments.EndsWith(';'))
        {
            strippedLeadingComments = strippedLeadingComments[..^1].TrimEnd();
        }

        return strippedLeadingComments;
    }

    private static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var builder = new StringBuilder();
        var inString = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
            {
                builder.Append(c);

                if (inString && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    builder.Append(sql[i + 1]);
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (c == ';' && !inString)
            {
                statements.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        if (builder.Length > 0)
        {
            statements.Add(builder.ToString());
        }

        return statements;
    }

    private static string StripLeadingComments(string sql)
    {
        var span = sql.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
            {
                index++;
            }

            if (index + 1 < span.Length && span[index] == '-' && span[index + 1] == '-')
            {
                index += 2;
                while (index < span.Length && span[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (index + 1 < span.Length && span[index] == '/' && span[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < span.Length && !(span[index] == '*' && span[index + 1] == '/'))
                {
                    index++;
                }

                if (index + 1 < span.Length)
                {
                    index += 2;
                }

                continue;
            }

            break;
        }

        return sql[index..];
    }

    private static string StripStringLiterals(string sql)
    {
        var builder = new StringBuilder(sql.Length);
        var inString = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
            {
                if (inString && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (!inString)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
