using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseTelemetry
{
    public const string MeterName = "DatabaseMcpServer.Execution";
    private static readonly Meter Meter = new(MeterName);
    public static readonly UpDownCounter<long> Active = Meter.CreateUpDownCounter<long>("dbmcp.active");
    public static readonly UpDownCounter<long> Queued = Meter.CreateUpDownCounter<long>("dbmcp.queued");
    public static readonly Counter<long> Rejected = Meter.CreateCounter<long>("dbmcp.rejected");
    public static readonly Histogram<double> QueueMs = Meter.CreateHistogram<double>("dbmcp.queue.duration", "ms");
    public static readonly Histogram<double> SqlMs = Meter.CreateHistogram<double>("dbmcp.sql.duration", "ms");
    public static readonly Histogram<double> ReadMs = Meter.CreateHistogram<double>("dbmcp.read.duration", "ms");
    public static readonly Histogram<double> RequestMs = Meter.CreateHistogram<double>("dbmcp.request.duration", "ms");
    public static readonly Histogram<double> SerializeMs = Meter.CreateHistogram<double>("dbmcp.serialize.duration", "ms");
    public static readonly Histogram<long> ResponseBytes = Meter.CreateHistogram<long>("dbmcp.response.bytes", "By");
    public static readonly Counter<long> Cancelled = Meter.CreateCounter<long>("dbmcp.cancelled");

    public static IDisposable MeasureRead() => new Measurement(ReadMs);
    public static IDisposable MeasureRequest() => new Measurement(RequestMs);

    private sealed class Measurement(Histogram<double> metric) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        public void Dispose() => metric.Record(Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    }

    public static string Serialize(Func<string> serialize)
    {
        var started = Stopwatch.GetTimestamp();
        var result = serialize();
        SerializeMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        ResponseBytes.Record(Encoding.UTF8.GetByteCount(result));
        return result;
    }

    public static byte[] Serialize(Func<byte[]> serialize)
    {
        var started = Stopwatch.GetTimestamp();
        var result = serialize();
        SerializeMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        ResponseBytes.Record(result.Length);
        return result;
    }
}
