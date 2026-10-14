using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticPublicationBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private static DiagnosticReport Report => new() { GeneratedAtUtc = Now.UtcDateTime };

    [Fact]
    public void PreparedReportPreservesAllFiveRegisteredIpcGates()
    {
        var input = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.11",
            Guid.Parse("f8b58df7-dc88-4ae8-b0e5-6dce6416e365"), false, false, 0, new(0, 0, 0), new(), Now);

        var prepared = DiagnosticReportSerializer.Prepare(DiagnosticReportPreparer.Prepare(input, Now));

        DiagnosticReportSerializer.ValidateWireJson(prepared.Json);
        using var document = JsonDocument.Parse(prepared.Json);
        var gates = document.RootElement.GetProperty("ipc").GetProperty("gatesRegistered");
        Assert.Equal(new[] { "getCapabilities", "getSnapshot", "reportOwnershipEvidence", "snapshotChanged", "snapshotWithdrawn" },
            gates.EnumerateObject().Select(property => property.Name));
        Assert.True(gates.GetProperty("getCapabilities").GetBoolean());
        Assert.True(gates.GetProperty("getSnapshot").GetBoolean());
        Assert.True(gates.GetProperty("snapshotChanged").GetBoolean());
        Assert.True(gates.GetProperty("snapshotWithdrawn").GetBoolean());
        Assert.True(gates.GetProperty("reportOwnershipEvidence").GetBoolean());
    }

    [Theory]
    [InlineData("dependencies")]
    [InlineData("performance")]
    [InlineData("fingerprints")]
    public void DynamicDiagnosticKeysMustBeSymbolsEvenWhenTheyAreNotProhibitedNames(string section)
    {
        var valid = WithKey(section, "Fixture.Metric");
        DiagnosticReportSerializer.ValidateWireJson(DiagnosticReportSerializer.Prepare(valid).Json);
        var invalid = WithKey(section, "bad/path");

        Assert.Throws<InvalidDataException>(() => DiagnosticReportSerializer.Prepare(invalid));
    }

    [Fact]
    public async Task ClosingAfterWorkerCompletionButBeforeUpdateRetainsThePriorAcceptedReport()
    {
        var next = false;
        var copies = new List<string>();
        await using var controller = new DiagnosticsController(_ => Task.FromResult(next
            ? Report with { Build = new() { PluginVersion = "0.2.0.0" } }
            : Report), new(), copies.Add);
        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await WaitForCompletedWorker(controller);
        controller.Update();
        var previous = Assert.IsType<PreparedDiagnosticReport>(controller.View.Report);

        next = true;
        controller.Dispatch(DiagnosticsIntent.GenerateReport);
        await WaitForCompletedWorker(controller);
        Assert.True(controller.View.Running);
        controller.Dispatch(DiagnosticsIntent.Close);
        controller.Update();

        Assert.Same(previous, controller.View.Report);
        Assert.False(controller.View.Running);
        Assert.Equal("NotStarted", controller.OwnedTask.State);
        controller.Dispatch(DiagnosticsIntent.CopyReport);
        Assert.Equal(previous.Json, Assert.Single(copies));
    }

    private static DiagnosticReport WithKey(string section, string key) => section switch
    {
        "dependencies" => Report with { Runtime = new() { Dependencies = ImmutableDictionary<string, string>.Empty.Add(key, "1.0") } },
        "performance" => Report with { Performance = ImmutableDictionary<string, DiagnosticMetric>.Empty.Add(key, new(1, new(0, 0, 0), new(0, 0, 0))) },
        "fingerprints" => Report with { CollectionPipeline = new() { Fingerprints = ImmutableDictionary<string, string?>.Empty.Add(key, new string('a', 64)) } },
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    private static async Task WaitForCompletedWorker(DiagnosticsController controller)
    {
        var started = Stopwatch.GetTimestamp();
        while (controller.OwnedTask.State != "Completed")
        {
            Assert.DoesNotContain(controller.OwnedTask.State, new[] { "Faulted", "Cancelled", "NotStarted" });
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3), "The report worker must finish without requiring Update to consume its result.");
            await Task.Delay(1);
        }
    }
}
