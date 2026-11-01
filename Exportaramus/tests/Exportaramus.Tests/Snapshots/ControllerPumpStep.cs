using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

internal static class ControllerPumpStep
{
    public static void Advance(SnapshotController controller, Action update)
    {
        var terminal = controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State is "Completed" or "Faulted" or "Cancelled";
        var priorStaleRejection = controller.DiagnosticOperation(0).StaleCompletionRejected;
        var before = Phase(controller);
        update();
        if (terminal && !priorStaleRejection)
        {
            // Completed current work must advance the phase, but may start another worker. After a stale
            // rejection, the phase may repeat; let the polling timeout detect a stall.
            Assert.NotEqual(before, Phase(controller));
        }
    }

    private static object Phase(SnapshotController controller) => (
        controller.Publication.Current,
        controller.Publication.GetCapabilities(),
        controller.Reports.Available,
        controller.Error,
        controller.ActiveContentId,
        controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State,
        controller.DiagnosticOperation(0) with
        {
            ElapsedMilliseconds = 0
        });
}
