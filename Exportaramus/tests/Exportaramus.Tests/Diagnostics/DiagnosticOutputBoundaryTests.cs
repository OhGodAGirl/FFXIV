using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.Diagnostics;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticOutputBoundaryTests(ITestOutputHelper output)
{
    private static DiagnosticReport Report => new() { GeneratedAtUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void DiagnosticWireAcceptsExactUtf8LimitAndRejectsOneAdditionalByte()
    {
        var accepted = DiagnosticReportSerializer.Prepare(Report);
        var exact = accepted.Json + new string(' ', DiagnosticReportSerializer.MaximumUtf8Bytes - accepted.Utf8Bytes);

        Assert.Equal(DiagnosticReportSerializer.MaximumUtf8Bytes, Encoding.UTF8.GetByteCount(exact));
        DiagnosticReportSerializer.ValidateWireJson(exact);
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.ValidateWireJson(exact + " "));

        Assert.Equal("DiagnosticOutputSizeLimitExceeded", error.Message);
        output.WriteLine("Valid diagnostic wire accepted at 262,144 UTF-8 bytes; 262,145 bytes rejected.");
    }

    [Fact]
    public void CanonicalDiagnosticSerializerHonorsExactByteBoundaryWithoutTruncatingFrames()
    {
        var exact = DiagnosticAtCanonicalSize(DiagnosticReportSerializer.MaximumUtf8Bytes);

        var accepted = DiagnosticReportSerializer.Prepare(exact);

        Assert.Equal(DiagnosticReportSerializer.MaximumUtf8Bytes, accepted.Utf8Bytes);
        Assert.Equal(accepted.Utf8Bytes, Encoding.UTF8.GetByteCount(accepted.Json));
        DiagnosticReportSerializer.ValidateWireJson(accepted.Json);
        Assert.Equal(20, accepted.Model.RecentFailures.Length);
        Assert.All(accepted.Model.RecentFailures, failure => Assert.Equal(32, failure.Frames.Length));
        for (var failure = 0; failure < exact.RecentFailures.Length; failure++)
            Assert.Equal(exact.RecentFailures[failure].Frames, accepted.Model.RecentFailures[failure].Frames);
        var over = DiagnosticAtCanonicalSize(DiagnosticReportSerializer.MaximumUtf8Bytes + 1);
        var overDocument = JsonSerializer.SerializeToElement(over with { FingerprintSha256 = new string('0', 64) },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.True(ContractSchemaRegistry.Default.Validate(overDocument, "exportaramus-diagnostics.schema.json"));
        Assert.Equal(DiagnosticReportSerializer.MaximumUtf8Bytes + 1, DiagnosticReportSerializer.Canonical(overDocument, false).Length);
        var error = Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(over));
        Assert.Equal("DiagnosticOutputSizeLimitExceeded", error.Message);
        output.WriteLine("Canonical typed report accepted at 262,144 bytes with all 640 frames retained; otherwise valid 262,145-byte report rejected.");
    }

    [Fact]
    public async Task OversizedReportGenerationRetainsPreviouslyAcceptedReportAndCopyBytes()
    {
        var next = DiagnosticAtCanonicalSize(DiagnosticReportSerializer.MaximumUtf8Bytes);
        string? copied = null;
        await using var controller = new DiagnosticsController(_ => Task.FromResult(next), new(), value => copied = value);
        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await DiagnosticWorkerTest.DrainAsync(controller);
        var previous = Assert.IsType<PreparedDiagnosticReport>(controller.View.Report);
        Assert.Equal(DiagnosticReportSerializer.MaximumUtf8Bytes, previous.Utf8Bytes);
        next = DiagnosticAtCanonicalSize(DiagnosticReportSerializer.MaximumUtf8Bytes + 1);

        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        controller.Dispatch(DiagnosticsIntent.CopyReport);
        Assert.Same(previous.Json, copied);
        await DiagnosticWorkerTest.DrainAsync(controller);

        Assert.Equal("Failed", controller.View.Status);
        Assert.Same(previous, controller.View.Report);
        Assert.True(controller.View.CanCopy);
        copied = null;
        controller.Dispatch(DiagnosticsIntent.CopyReport);
        Assert.Same(previous.Json, copied);
    }

    private static DiagnosticReport DiagnosticAtCanonicalSize(int target)
    {
        var failure = DiagnosticRedactionPolicy.Failure(Report.GeneratedAtUtc, "Storage", "Write", "WriteFailed", null);
        var failures = Enumerable.Range(0, 20).Select(_ => failure with
        {
            Frames = Enumerable.Range(0, 32).Select(_ => new DiagnosticFrame("A", "B", "C", "Source.cs", 1)).ToImmutableArray()
        }).ToArray();
        var report = Report with { RecentFailures = failures.ToImmutableArray() };
        // Measure the actual accepted serializer output once. Expanding unescaped ASCII within
        // schema-valid frame fields changes its canonical byte size one-for-one; hashes stay 64 bytes.
        var baseline = DiagnosticReportSerializer.Prepare(report).Utf8Bytes;
        var remaining = target - baseline;
        Assert.InRange(remaining, 0, 20 * 32 * 3 * 255);
        for (var item = 0; item < failures.Length; item++)
        {
            var frames = failures[item].Frames.ToArray();
            for (var frame = 0; frame < frames.Length; frame++)
            {
                var assembly = Math.Min(255, remaining); remaining -= assembly;
                var type = Math.Min(255, remaining); remaining -= type;
                var method = Math.Min(255, remaining); remaining -= method;
                frames[frame] = frames[frame] with
                {
                    Assembly = new string('A', 1 + assembly),
                    Type = new string('B', 1 + type),
                    Method = new string('C', 1 + method)
                };
            }
            failures[item] = failures[item] with { Frames = frames.ToImmutableArray() };
        }
        Assert.Equal(0, remaining);
        return report with { RecentFailures = failures.ToImmutableArray() };
    }

}
