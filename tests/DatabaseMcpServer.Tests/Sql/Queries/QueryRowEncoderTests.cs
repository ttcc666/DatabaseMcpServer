using System.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tests;

public class QueryRowEncoderTests
{
    [Fact]
    public async Task NamesAndScalars_MatchSerializerBytes()
    {
        var values = new Dictionary<string, object?>
        {
            ["BigId"] = 9007199254740993L, ["minimum"] = long.MinValue, ["unsigned"] = ulong.MaxValue,
            ["amount"] = 123456789.123456789m, ["scaled"] = 1.2300m, ["flag"] = true,
            ["integer"] = 7, ["short"] = (short)-3, ["byte"] = (byte)255, ["single"] = 1.25f,
            ["double"] = -0.0d, ["character"] = '汉', ["empty"] = "",
            ["汉😀\"\\\n"] = "汉😀\"\\\n\u0000 <>&", ["surrogate"] = "\uD800",
            ["bytes"] = new byte[] { 0, 128, 255 }, ["missing"] = null,
            ["at"] = new DateTime(2026, 11, 23, 8, 15, 1, DateTimeKind.Utc).AddTicks(1234567),
            ["offset"] = new DateTimeOffset(2026, 11, 23, 8, 15, 1, TimeSpan.FromHours(8)).AddTicks(1234567),
            ["day"] = new DateOnly(2026, 11, 23), ["time"] = new TimeOnly(8, 15, 1),
            ["duration"] = TimeSpan.FromTicks(1234567), ["guid"] = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ["enum"] = DayOfWeek.Monday
        };
        using var table = new DataTable();
        foreach (var name in values.Keys) table.Columns.Add(name, typeof(object));
        table.Rows.Add(values.Values.Select(value => value ?? DBNull.Value).ToArray());
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        Assert.True(reader.Read());
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(values, QueryRowEncoder.Options),
            (await encoder.ReadAsync(8192, default)).Utf8Json);
    }

    [Fact]
    public async Task EncodedPropertyNames_UseExactUtf8Budget()
    {
        var values = new Dictionary<string, object?> { ["汉😀\"\\\n"] = "é", ["empty"] = "" };
        var expected = JsonSerializer.SerializeToUtf8Bytes(values, QueryRowEncoder.Options);
        using var table = new DataTable();
        foreach (var name in values.Keys) table.Columns.Add(name, typeof(string));
        table.Rows.Add(values.Values.ToArray());
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        Assert.True(reader.Read());
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(expected.Length - 1, default));
        Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
        Assert.Equal(expected, (await encoder.ReadAsync(expected.Length, default)).Utf8Json);
        Assert.Equal(expected, (await encoder.ReadAsync(expected.Length + 1, default)).Utf8Json);
    }

    [Fact]
    public async Task NullFirstRow_DoesNotFixLaterValueType()
    {
        using var table = new DataTable();
        table.Columns.Add("v", typeof(object));
        foreach (var value in new object?[] { null, 9007199254740993L, "汉😀", 1.2300m, new byte[] { 0, 128, 255 } })
            table.Rows.Add(value ?? DBNull.Value);
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        foreach (var expected in new object?[] { null, 9007199254740993L, "汉😀", 1.2300m, new byte[] { 0, 128, 255 } })
        {
            Assert.True(reader.Read());
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["v"] = expected }, QueryRowEncoder.Options),
                (await encoder.ReadAsync(4096, default)).Utf8Json);
        }
        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task NonFiniteScalar_ThrowsSerializerErrorAndResetsNextRow(int kind)
    {
        object[] cases = [double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        var reference = Record.Exception(() => JsonSerializer.SerializeToUtf8Bytes(cases[kind], QueryRowEncoder.Options));
        Assert.NotNull(reference);
        using var table = new DataTable();
        table.Columns.Add("number", typeof(object));
        table.Rows.Add(cases[kind]);
        table.Rows.Add(7);
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        var values = new Dictionary<string, object?>();
        Assert.True(reader.Read());
        var error = await Record.ExceptionAsync(() => encoder.ReadAsync(4096, default, values));
        Assert.NotNull(error);
        Assert.Equal(reference.GetType(), error.GetType());
        Assert.Empty(values);
        Assert.True(reader.Read());
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("{\"number\":7}"),
            (await encoder.ReadAsync(4096, default, values)).Utf8Json);
        Assert.Equal(7, values["number"]);
    }

    [Fact]
    public async Task EncodedRowsRemainIndependentAfterReset()
    {
        using var table = new DataTable();
        table.Columns.Add("id", typeof(long));
        table.Columns.Add("label", typeof(string));
        table.Rows.Add(9007199254740993L, "汉😀");
        table.Rows.Add(2L, "second");
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        Assert.True(reader.Read());
        var first = await encoder.ReadAsync(8192, default);
        Assert.True(reader.Read());
        var second = await encoder.ReadAsync(8192, default);
        using var firstJson = JsonDocument.Parse(first.Utf8Json);
        using var secondJson = JsonDocument.Parse(second.Utf8Json);
        Assert.Equal(9007199254740993L, firstJson.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("汉😀", firstJson.RootElement.GetProperty("label").GetString());
        Assert.Equal(2L, secondJson.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("second", secondJson.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task FailedRowDoesNotContaminateNextRow()
    {
        using var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("label", typeof(string));
        table.Rows.Add(1, new string('x', 5000));
        table.Rows.Add(7, "ok");
        using var reader = table.CreateDataReader();
        using var encoder = new QueryRowEncoder(reader);
        var values = new Dictionary<string, object?>();
        Assert.True(reader.Read());
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => encoder.ReadAsync(64, default, values));
        Assert.Equal(DatabaseErrorCode.ResponseLimitExceeded, error.ErrorCode);
        Assert.Empty(values);
        Assert.True(reader.Read());
        var result = await encoder.ReadAsync(64, default, values);
        using var json = JsonDocument.Parse(result.Utf8Json);
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("ok", json.RootElement.GetProperty("label").GetString());
        Assert.Equal(7, values["id"]);
        Assert.Equal("ok", values["label"]);
    }

    [Fact]
    public async Task NewResultSetDoesNotReusePreviousColumnPlan()
    {
        using var first = new DataTable();
        first.Columns.Add("id", typeof(int)); first.Rows.Add(1);
        using var second = new DataTable();
        second.Columns.Add("label", typeof(string)); second.Columns.Add("missing", typeof(string));
        second.Rows.Add("汉", DBNull.Value);
        using var reader = new DataTableReader([first, second]);
        using (var encoder = new QueryRowEncoder(reader))
        {
            Assert.True(reader.Read());
            using var json = JsonDocument.Parse((await encoder.ReadAsync(64, default)).Utf8Json);
            Assert.Equal(1, json.RootElement.GetProperty("id").GetInt32());
        }
        Assert.True(reader.NextResult());
        using (var encoder = new QueryRowEncoder(reader))
        {
            Assert.True(reader.Read());
            using var json = JsonDocument.Parse((await encoder.ReadAsync(64, default)).Utf8Json);
            Assert.False(json.RootElement.TryGetProperty("id", out _));
            Assert.Equal("汉", json.RootElement.GetProperty("label").GetString());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("missing").ValueKind);
        }
    }

    [Fact]
    public async Task JsonlExport_PreservesUtf8AndExactFileBoundary()
    {
        using var fixture = DatabaseRoleTests.Create();
        var service = fixture.Host.Services.GetRequiredService<SqlExportService>();
        const int maximum = 1024 * 1024;
        async Task<JsonElement> Export(MemoryStream destination, int length) => JsonSerializer.SerializeToElement(
            await service.Export(destination, "SELECT @value AS value",
                [new() { Name = "value", Value = JsonSerializer.SerializeToElement(new string('x', length)) }],
                "jsonl", null, 2, 1, null, default), JsonSerializationDefaults.CompactCamelCase);
        using var exact = new MemoryStream();
        var success = await Export(exact, maximum - 13);
        Assert.True(success.GetProperty("success").GetBoolean(), success.ToString());
        Assert.Equal(maximum, exact.Length);
        Assert.Equal((byte)'\n', exact.ToArray()[^1]);
        using var tooLarge = new MemoryStream();
        var error = await Export(tooLarge, maximum - 12);
        Assert.False(error.GetProperty("success").GetBoolean());
        Assert.Equal((int)DatabaseErrorCode.ResponseLimitExceeded, error.GetProperty("errorCode").GetInt32());
        Assert.Equal(0, tooLarge.Length);
        using var unicode = new MemoryStream();
        var result = JsonSerializer.SerializeToElement(await service.Export(unicode,
            "SELECT '汉😀' AS label,X'0080FF' AS bytes,NULL AS missing", [], "jsonl", null, 2, 1, null, default),
            JsonSerializationDefaults.CompactCamelCase);
        Assert.True(result.GetProperty("success").GetBoolean());
        using var json = JsonDocument.Parse(unicode.ToArray());
        Assert.Equal("汉😀", json.RootElement.GetProperty("label").GetString());
        Assert.Equal(new byte[] { 0, 128, 255 }, json.RootElement.GetProperty("bytes").GetBytesFromBase64());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task CsvExport_DoesNotReusePreviousRowValues()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var output = new MemoryStream();
        var result = JsonSerializer.SerializeToElement(await fixture.Host.Services.GetRequiredService<SqlExportService>()
            .Export(output, "SELECT 7 AS id,'汉,\"x\"' AS label UNION ALL SELECT 8,NULL", [], "csv", null, 3, 1, null, default),
            JsonSerializationDefaults.CompactCamelCase);
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("\"id\",\"label\"\n\"7\",\"汉,\"\"x\"\"\"\n\"8\",\"\"\n", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("SELECT 1 AS x,2 AS X")]
    [InlineData("SELECT 1 AS \"\"")]
    public async Task Export_RejectsAmbiguousColumnNames(string sql)
    {
        using var fixture = DatabaseRoleTests.Create();
        using var output = new MemoryStream();
        var result = JsonSerializer.SerializeToElement(await fixture.Host.Services.GetRequiredService<SqlExportService>()
            .Export(output, sql, [], "jsonl", null, 3, 1, null, default), JsonSerializationDefaults.CompactCamelCase);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal((int)DatabaseErrorCode.InvalidParameters, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(0, output.Length);
    }
}
