using System.Collections.Immutable;
using Exportaramus.Features.Diagnostics;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Diagnostics;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class DiagnosticAdmissionBudgetTests(ITestOutputHelper output)
{
    // Invalid metadata must be rejected before allocating canonical/output copies of a large report.
    // The prepared object graph is outside the measurement; 64 KiB permits validation bookkeeping.
    private const long AdmissionAllocationBudget = 64 * 1024;
    private static DiagnosticReport LargeReport()
    {
        var failure = DiagnosticRedactionPolicy.Failure(new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), "Storage", "Write", "WriteFailed", null)
            with
        { Frames = Enumerable.Repeat(new DiagnosticFrame(new('A', 64), new('B', 64), new('C', 64), "Source.cs", 1), 32).ToImmutableArray() };
        return new() { GeneratedAtUtc = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), RecentFailures = Enumerable.Repeat(failure, 20).ToImmutableArray() };
    }

    [Theory]
    [InlineData("failures")]
    [InlineData("sources")]
    [InlineData("tasks")]
    public void OversizedTypedArraysAreRejectedBeforeSerializingTheReport(string field)
    {
        var report = LargeReport();
        var invalid = field switch
        {
            "failures" => report with { RecentFailures = report.RecentFailures.Add(report.RecentFailures[0]) },
            "sources" => report with { SourceHealth = Enumerable.Repeat(new DiagnosticSource("mounts", "Supported", "Fresh", "Complete", "Observed", 0, 0, "None"), 65).ToImmutableArray() },
            "tasks" => report with { Lifecycle = new() { OwnedTasks = Enumerable.Repeat(new DiagnosticTask("Storage", "Running"), 33).ToImmutableArray() } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        AssertAdmissionBudget(invalid);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("dependency")]
    [InlineData("fingerprint")]
    [InlineData("rule")]
    [InlineData("task")]
    [InlineData("omission")]
    [InlineData("source")]
    [InlineData("assembly")]
    [InlineData("type")]
    [InlineData("method")]
    [InlineData("filenameForward")]
    [InlineData("filenameBack")]
    public void ProhibitedTypedMetadataIsRejectedBeforeSerializingTheReport(string field)
    {
        var report = LargeReport();
        const string prohibited = "alice@example.com";
        var frame = report.RecentFailures[0].Frames[0];
        var invalid = field switch
        {
            "incomplete" => report with { ReportStatus = "Partial", IncompleteSections = [prohibited] },
            "dependency" => report with { Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add("Fixture", prohibited) } },
            "fingerprint" => report with { CollectionPipeline = new() { Fingerprints = ImmutableDictionary<string, string?>.Empty.Add("Fixture", prohibited) } },
            "rule" => report with { Redaction = new() { AppliedRules = [prohibited] } },
            "task" => report with { Lifecycle = new() { OwnedTasks = [new("Storage", prohibited)] } },
            "omission" => report with { CollectionPipeline = new() { Omissions = [new("mounts", prohibited)] } },
            "source" => report with { SourceHealth = [new("mounts", "Supported", "Fresh", "Complete", "Observed", 0, 0, prohibited)] },
            "assembly" => WithFrame(report, frame with { Assembly = prohibited }),
            "type" => WithFrame(report, frame with { Type = prohibited }),
            "method" => WithFrame(report, frame with { Method = prohibited }),
            "filenameForward" => WithFrame(report, frame with { SourceFileName = "folder/Source.cs" }),
            "filenameBack" => WithFrame(report, frame with { SourceFileName = "folder\\Source.cs" }),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        AssertAdmissionBudget(invalid);
    }

    private static DiagnosticReport WithFrame(DiagnosticReport report, DiagnosticFrame frame) => report with
    {
        RecentFailures = report.RecentFailures.SetItem(0, report.RecentFailures[0] with { Frames = report.RecentFailures[0].Frames.SetItem(0, frame) })
    };

    private void AssertAdmissionBudget(DiagnosticReport invalid)
    {
        // Warm the exception and regex paths without counting JIT/static initialization.
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(invalid));
        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(invalid));
        Exception? error = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        try { DiagnosticReportSerializer.Prepare(invalid); }
        catch (Exception exception) { error = exception; }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<InvalidDataException>(error);
        output.WriteLine("Rejected report allocated {0} bytes; admission budget {1} bytes.", allocated, AdmissionAllocationBudget);
        Assert.InRange(allocated, 0, AdmissionAllocationBudget);
    }
}
