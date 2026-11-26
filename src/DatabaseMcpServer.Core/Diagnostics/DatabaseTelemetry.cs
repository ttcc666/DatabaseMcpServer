using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseTelemetry
{
    public const string MeterName = "DatabaseMcpServer.Execution";
    public const string ActivitySourceName = "DatabaseMcpServer.Execution";
    private static readonly Meter Meter = new(MeterName);
    private static readonly ActivitySource Activities = new(ActivitySourceName);
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
    public static readonly Counter<long> RequestTimeouts = Meter.CreateCounter<long>("dbmcp.request.timeouts");
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("dbmcp.metadata.cache.hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("dbmcp.metadata.cache.misses");
    public static readonly Histogram<double> CancellationMs = Meter.CreateHistogram<double>("dbmcp.cancellation.completion", "ms");

    // The fallback supplies a correlation ID for logs even without a tracing subscriber.
    public static Activity StartTool() => Activities.StartActivity("dbmcp.tool")
        ?? new Activity("dbmcp.tool").SetIdFormat(ActivityIdFormat.W3C).Start();
    public static Activity? StartAdmission() => Activities.StartActivity("dbmcp.admission");

    public static IDisposable MeasureRead() => new Measurement(ReadMs);
    public static IDisposable MeasureRequest() => new Measurement(RequestMs);
    public static IDisposable MeasureSerialization() => new Measurement(SerializeMs);
    public static IDisposable MeasureCancellation(CancellationToken token) => new CancellationMeasurement(token);

    private sealed class CancellationMeasurement : IDisposable
    {
        private long _cancelledAt;
        private readonly CancellationTokenRegistration _registration;
        public CancellationMeasurement(CancellationToken token)
            => _registration = token.Register(() => Interlocked.CompareExchange(ref _cancelledAt, Stopwatch.GetTimestamp(), 0));
        public void Dispose()
        {
            _registration.Dispose();
            var started = Interlocked.Read(ref _cancelledAt);
            if (started != 0) CancellationMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private sealed class Measurement(Histogram<double> metric) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly Activity? _activity = Activities.StartActivity(metric.Name);
        public void Dispose()
        {
            try { metric.Record(Stopwatch.GetElapsedTime(_started).TotalMilliseconds); }
            finally { _activity?.Dispose(); }
        }
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
