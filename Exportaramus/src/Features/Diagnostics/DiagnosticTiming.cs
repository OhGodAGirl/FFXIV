using System.Diagnostics;

namespace Exportaramus.Features.Diagnostics;

/// <summary>Measures thread allocations for synchronous work and process-wide allocations for async
/// work.</summary>
public readonly struct DiagnosticTiming(DiagnosticMeasurements measurements, string metric, bool processAllocation = false) : IDisposable
{
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly long allocated = processAllocation ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread();
    public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    public long AllocatedBytes => (processAllocation ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread()) - allocated;
    public void Dispose() => measurements.Record(metric, ElapsedMilliseconds, AllocatedBytes);
}
