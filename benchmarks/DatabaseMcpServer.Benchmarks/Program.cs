using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using SqlSugar;

// Isolated, opt-in microbenchmark. Samples are observations, never CI timing assertions.
if (args.Length != 1) throw new ArgumentException("Specify an output JSON path.");
var path = Path.Combine(Path.GetTempPath(), "dbmcp-bench-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    using var client = new SqlSugarClient(new ConnectionConfig { DbType = DbType.Sqlite,
        ConnectionString = $"Data Source={path};Pooling=False", IsAutoCloseConnection = true });
    var context = new DatabaseClientContext(client, false, "benchmark") { DatabaseName = "benchmark", DatabaseType = "Sqlite" };
    client.Ado.ExecuteCommand("CREATE TABLE sample(id integer PRIMARY KEY,value text); WITH RECURSIVE n(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM n WHERE id<10000) INSERT INTO sample SELECT id,hex(zeroblob(64)) FROM n");
    client.Ado.ExecuteCommand("CREATE TABLE large(value text,blob_value blob); INSERT INTO large VALUES(hex(zeroblob(4194304)),zeroblob(8388608))");
    async Task Query(string sql, int rows, int bytes, bool truncated)
    {
        var result = await new BoundedQueryExecutor(rows + 1, bytes, 1024).QueryResultAsync(context, sql, [], default);
        if (result.RowCount != rows || result.Truncated != truncated) throw new InvalidOperationException("Benchmark result changed.");
    }
    var scenarios = new (string Name, int Operations, Func<Task> Run)[]
    {
        ("small-query", 100, () => Query("SELECT id,value FROM sample WHERE id=1", 1, 4096, false)),
        ("many-rows", 1, () => Query("SELECT * FROM sample ORDER BY id", 10000, 4*1024*1024, false)),
        ("wide-row", 20, () => Query("SELECT " + string.Join(',', Enumerable.Range(0,128).Select(i => $"'{new string('x',64)}' AS c{i}")), 1, 32768, false)),
        ("oversized-text", 1, () => Query("SELECT value FROM large", 0, 4096, true)),
        ("oversized-blob", 1, () => Query("SELECT blob_value FROM large", 0, 4096, true))
    };
    var reports = new List<object>();
    foreach (var (name, operations, run) in scenarios)
    {
        for (var i = 0; i < 3; i++) await run();
        var samples = new List<(double Ms, long Bytes)>();
        for (var sample = 0; sample < 20; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < operations; i++) await run();
            samples.Add((Stopwatch.GetElapsedTime(start).TotalMilliseconds / operations,
                (GC.GetTotalAllocatedBytes(true) - allocated) / operations));
        }
        var sorted = samples.Select(s => s.Ms).Order().ToArray();
        reports.Add(new { scenario = name, operationsPerSample = operations, samples = samples.Select(s => new { elapsedMs = s.Ms, allocatedBytes = s.Bytes }),
            p50Ms = sorted[9], p95Ms = sorted[18], meanAllocatedBytes = samples.Average(s => s.Bytes), throughputPerSecond = 1000 / samples.Average(s => s.Ms) });
        Console.WriteLine($"{name}: P95 {sorted[18]:F3} ms, mean allocated {samples.Average(s => s.Bytes):F0} B/op");
    }
    var output = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(),
        measuredAt = DateTimeOffset.UtcNow, processorCount = Environment.ProcessorCount, provider = "SQLite", samples = 20,
        allocationScope = "GC.GetTotalAllocatedBytes process-wide; excludes native buffers; isolated console", reports }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { File.Delete(path); }
