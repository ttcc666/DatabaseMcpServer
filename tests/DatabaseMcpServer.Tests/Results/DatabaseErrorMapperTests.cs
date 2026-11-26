using System.Collections;
using System.Data.Common;
using System.Data.OscarClient;
using System.Reflection;
using System.Text;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMcpServer.Tests;

public class DatabaseErrorMapperTests
{
    public static IEnumerable<object[]> SqlStateCases()
    {
        foreach (var provider in new[] { "Kdbndp", "Nhgdb", "Oscar", "Npgsql", "OpenGauss" })
        foreach (var (state, expected) in new[]
        {
            ("57014", DatabaseErrorCode.CommandTimeout),
            ("08006", DatabaseErrorCode.ConnectionFailed),
            ("28P01", DatabaseErrorCode.ConnectionFailed),
            ("3D000", DatabaseErrorCode.ConnectionFailed),
            ("42P01", DatabaseErrorCode.QueryExecutionFailed),
            ("", DatabaseErrorCode.QueryExecutionFailed)
        })
        {
            yield return [provider, state, expected, false];
            if (expected != DatabaseErrorCode.QueryExecutionFailed)
                yield return [provider, state, expected, true];
        }
    }

    [Theory]
    [MemberData(nameof(SqlStateCases))]
    public void Map_UsesProviderSqlState(string provider, string sqlState, DatabaseErrorCode expected, bool wrapped)
    {
        DbException error;
        if (provider == "Oscar")
        {
            // The driver exposes no public error constructors; feed its SQLSTATE/message wire representation.
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes($"{sqlState}\0sensitive timeout text\0"));
            var detail = (OscarError)Activator.CreateInstance(typeof(OscarError), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [payload], null)!;
            error = (OscarException)Activator.CreateInstance(typeof(OscarException), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new ArrayList { detail }], null)!;
        }
        else
        {
            error = provider switch
            {
                "Kdbndp" => new Kdbndp.KingbaseException { SqlState = sqlState, MessageText = "sensitive timeout text" },
                "Nhgdb" => new Nhgdb.HighgoException("sensitive timeout text", "ERROR", "ERROR", sqlState),
                "Npgsql" => new Npgsql.PostgresException("sensitive timeout text", "ERROR", "ERROR", sqlState),
                "OpenGauss" => new OpenGauss.NET.PostgresException("sensitive timeout text", "ERROR", "ERROR", sqlState),
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            };
        }

        var result = DatabaseErrorMapper.Map(wrapped ? new InvalidOperationException("wrapper", error) : error, NullLogger.Instance);

        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorCode);
        if (expected != DatabaseErrorCode.QueryExecutionFailed)
            Assert.DoesNotContain("sensitive", result.ErrorMessage);
    }

    [Theory]
    [InlineData(1013, DatabaseErrorCode.CommandTimeout)]
    [InlineData(1017, DatabaseErrorCode.ConnectionFailed)]
    [InlineData(12541, DatabaseErrorCode.ConnectionFailed)]
    [InlineData(942, DatabaseErrorCode.QueryExecutionFailed)]
    public void Map_ClassifiesOracleErrorNumbers(int number, DatabaseErrorCode expected)
    {
        var error = (OracleException)Activator.CreateInstance(typeof(OracleException), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [number, "review", "review", "sensitive timeout text", -1], null)!;

        var result = DatabaseErrorMapper.Map(error, NullLogger.Instance);

        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorCode);
        if (expected != DatabaseErrorCode.QueryExecutionFailed)
            Assert.DoesNotContain("sensitive", result.ErrorMessage);
    }
}
