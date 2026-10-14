using System.Diagnostics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed class CollectionDataIpcProvider : IDisposable
{
    private readonly ICallGateProvider<string> capabilities;
    private readonly ICallGateProvider<string> snapshot;
    private readonly ICallGateProvider<string, object?> changed;
    private readonly ICallGateProvider<string, object?> withdrawn;
    private readonly ICallGateProvider<string, uint, string, string> report;
    private readonly SnapshotController controller;
    private bool disposed;
    private readonly bool extended;
    private readonly bool beasts;

    public CollectionDataIpcProvider(IDalamudPluginInterface pluginInterface, SnapshotController controller, DiagnosticMeasurements measurements, bool extended = false, bool beasts = false)
    {
        this.controller = controller; this.extended = extended; this.beasts = beasts;
        string Gate(string legacy) => beasts ? legacy.Replace("Exportaramus.CollectionData.", "Exportaramus.CollectionData.V1_2.", StringComparison.Ordinal) : extended ? legacy.Replace("Exportaramus.CollectionData.", "Exportaramus.CollectionData.V1_1.", StringComparison.Ordinal) : legacy;
        capabilities = pluginInterface.GetIpcProvider<string>(Gate(CollectionDataGates.GetCapabilities));
        snapshot = pluginInterface.GetIpcProvider<string>(Gate(CollectionDataGates.GetSnapshot));
        changed = pluginInterface.GetIpcProvider<string, object?>(Gate(CollectionDataGates.SnapshotChanged));
        withdrawn = pluginInterface.GetIpcProvider<string, object?>(Gate(CollectionDataGates.SnapshotWithdrawn));
        report = pluginInterface.GetIpcProvider<string, uint, string, string>(Gate(CollectionDataGates.ReportOwnershipEvidence));
        try
        {
            capabilities.RegisterFunc(() =>
            {
                using var timing = new DiagnosticTiming(measurements, "GetCapabilities");
                return beasts ? controller.Publication.GetCapabilitiesV12() : extended ? controller.Publication.GetCapabilitiesV11() : controller.Publication.GetCapabilities();
            });
            snapshot.RegisterFunc(() =>
            {
                using var timing = new DiagnosticTiming(measurements, "GetSnapshot");
                return beasts ? controller.Publication.GetSnapshotV12() : extended ? controller.Publication.GetSnapshotV11() : controller.Publication.GetSnapshot();
            });
            report.RegisterFunc((contentId, itemId, reporter) =>
            {
                using var timing = new DiagnosticTiming(measurements, "ReportGate");
                var start = Stopwatch.GetTimestamp();
                var result = controller.Reports.TryEnqueue(contentId, itemId, reporter, (double)start / Stopwatch.Frequency, DateTimeOffset.UtcNow);
                measurements.CountReport(result == "Accepted", result == "RateLimited");
                return result;
            });
            if (beasts) { controller.ChangedV12 += SendChanged; controller.WithdrawnV12 += SendWithdrawn; }
            else if (extended) { controller.ChangedV11 += SendChanged; controller.WithdrawnV11 += SendWithdrawn; }
            else { controller.Changed += SendChanged; controller.Withdrawn += SendWithdrawn; }
        }
        catch { Dispose(); throw; }
    }
    // Dalamud owns subscriber dispatch. Catch its aggregate failure so accepted publication survives.
    // API 15 offers no supported per-subscriber dispatch or timeout facility (documented discrepancy).
    private void SendChanged(string json) { try { changed.SendMessage(json); } catch (Exception) { } }
    private void SendWithdrawn(string json) { try { withdrawn.SendMessage(json); } catch (Exception) { } }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (beasts) { controller.ChangedV12 -= SendChanged; controller.WithdrawnV12 -= SendWithdrawn; }
        else if (extended) { controller.ChangedV11 -= SendChanged; controller.WithdrawnV11 -= SendWithdrawn; }
        else { controller.Changed -= SendChanged; controller.Withdrawn -= SendWithdrawn; }
        try { report.UnregisterFunc(); } finally { try { snapshot.UnregisterFunc(); } finally { capabilities.UnregisterFunc(); } }
    }
}
