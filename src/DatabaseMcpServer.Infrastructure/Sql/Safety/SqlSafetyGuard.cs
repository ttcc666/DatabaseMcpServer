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
        @"\b(INSERT|UPDATE|DELETE|MERGE|REPLACE|UPSERT|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ReadOnlyPragmas = new(StringComparer.OrdinalIgnoreCase)
    {
        "application_id", "auto_vacuum", "automatic_index", "busy_timeout", "cache_size",
        "collation_list", "compile_options", "data_version", "database_list", "encoding",
        "foreign_keys", "freelist_count", "function_list", "journal_mode", "journal_size_limit",
        "locking_mode", "max_page_count", "mmap_size", "module_list", "page_count", "page_size",
        "pragma_list", "query_only", "read_uncommitted", "recursive_triggers", "schema_version",
        "secure_delete", "synchronous", "temp_store", "threads", "trusted_schema", "user_version"
    };

    private static readonly HashSet<string> ReadOnlyParameterizedPragmas = new(StringComparer.OrdinalIgnoreCase)
    {
        "table_info", "table_xinfo", "table_list", "index_info", "index_xinfo", "index_list",
        "foreign_key_list", "foreign_key_check", "integrity_check", "quick_check"
    };

    private const string PragmaIdentifier = """(?:[A-Za-z_][A-Za-z0-9_]*|"(?:""|[^"])*"|'(?:''|[^'])*'|`(?:``|[^`])*`|\[[^\]]+\])""";
    private const string PragmaArgument = "(?:" + PragmaIdentifier + @"|[+-]?\d+)";
    private static readonly Regex ReadOnlyPragmaRegex = new(
        @"^PRAGMA\s+(?:" + PragmaIdentifier + @"\s*\.\s*)?(?<name>" + PragmaIdentifier +
        @")\s*(?<argument>=\s*" + PragmaArgument + @"|\(\s*" + PragmaArgument + @"\s*\))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExecutableCommentRegex = new(@"/\*(?:!|M!)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        if (ExecutableCommentRegex.IsMatch(sql) || databaseHelper.DetectDangerousOperation(sql))
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

    public static void EnsureExplainableSql(string sql, IDatabaseHelperService helper)
    {
        EnsureReadOnlySql(sql, helper);
        var statement = SplitStatements(sql).Select(NormalizeSql).Single(s => s.Length > 0);
        if (!Regex.IsMatch(statement, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "估算计划只接受 SELECT/WITH 查询，不接受 EXPLAIN 或控制语句。");
    }

    public static void EnsureAtomicDml(string sql)
    {
        EnsureDataModificationSql(sql);
        var statements = SplitStatements(sql).Select(NormalizeSql).Where(s => s.Length > 0).ToArray();
        if (statements.Length != 1 || !Regex.IsMatch(statements[0], @"^(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase) ||
            Regex.Matches(StripStringLiterals(statements[0]), @"\b(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase).Count != 1 ||
            Regex.IsMatch(StripStringLiterals(statements[0]), @"\b(BEGIN|COMMIT|ROLLBACK|SAVEPOINT|TRANSACTION|VACUUM|ATTACH|DETACH|PRAGMA|COPY|EXEC|EXECUTE|CALL|OPENROWSET|OPENQUERY|WAITFOR|DBCC|KILL|USE|RECONFIGURE|BACKUP|RESTORE|SHUTDOWN|IMPLICIT_TRANSACTIONS|XACT_ABORT|IDENTITY_INSERT)\b", RegexOptions.IgnoreCase))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "atomic 仅接受每项一条 INSERT/UPDATE/DELETE，不接受脚本、DDL 或事务控制。");
    }

    // Restricted raw command mode deliberately accepts only standalone DML.
    // Procedures and scripts can change schema indirectly and require schema-admin.
    public static void EnsureDataModificationSql(string sql)
    {
        var statements = SplitStatements(sql).Select(NormalizeSql).Where(s => s.Length > 0).ToArray();
        if (statements.Length == 0 || ExecutableCommentRegex.IsMatch(sql) || statements.Any(statement =>
            !Regex.IsMatch(statement, @"^(INSERT|UPDATE|DELETE|MERGE|REPLACE|UPSERT)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(StripStringLiterals(statement), @"\b(CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL|PRAGMA|ATTACH|DETACH|COPY|GO)\b", RegexOptions.IgnoreCase)))
            throw new DatabaseMcpException(DatabaseErrorCode.AccessDenied,
                "read-write 模式仅允许独立 DML；架构、脚本和存储过程需要 schema-admin。");
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

        if (firstToken.Equals("PRAGMA", StringComparison.OrdinalIgnoreCase))
        {
            var pragma = ReadOnlyPragmaRegex.Match(statement);
            var name = pragma.Groups["name"].Value.Trim('\'', '"', '`', '[', ']');
            if (!pragma.Success ||
                !(ReadOnlyParameterizedPragmas.Contains(name) ||
                  (ReadOnlyPragmas.Contains(name) && !pragma.Groups["argument"].Success)))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation,
                    "只读工具仅允许读取已知 PRAGMA；赋值和具有副作用的 PRAGMA 已拒绝执行。");
            }

            return;
        }

        var sqlWithoutStringLiterals = StripStringLiterals(statement);
        // Some SQLite PRAGMAs execute during preparation, even when prefixed with EXPLAIN.
        if (ForbiddenWriteKeywordRegex.IsMatch(sqlWithoutStringLiterals) ||
            Regex.IsMatch(sqlWithoutStringLiterals, @"\bPRAGMA\b", RegexOptions.IgnoreCase))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "只读工具检测到写操作关键字，已拒绝执行。");
        }
    }

    private static string QuoteIdentifierSegment(string segment, DbType dbType)
    {
        return dbType switch
        {
            DbType.SqlServer => $"[{segment.Replace("]", "]]", StringComparison.Ordinal)}]",
            DbType.MySql or DbType.MySqlConnector or DbType.ClickHouse or DbType.Tidb or DbType.TDSQL or DbType.Doris or DbType.OceanBase or DbType.PolarDB or DbType.GoldenDB =>
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
        var quote = '\0';

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (quote == '\0' && i + 1 < sql.Length)
            {
                if (c == '-' && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n')
                    {
                        i++;
                    }

                    builder.Append(' ');
                    continue;
                }

                if (c == '/' && sql[i + 1] == '*')
                {
                    var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? sql.Length : end + 1;
                    builder.Append(' ');
                    continue;
                }
            }

            if (quote != '\0')
            {
                builder.Append(c);
                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        builder.Append(sql[++i]);
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (c is '\'' or '"' or '`' or '[')
            {
                quote = c == '[' ? ']' : c;
            }

            if (c == ';')
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
        var quote = '\0';

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (c is '\'' or '"' or '`' or '[')
            {
                quote = c == '[' ? ']' : c;
                builder.Append(' ');
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
