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
            // These fixture pumps expect current work to advance its public phase. A new worker
            // is allowed; do not require NotStarted. After an earlier stale rejection, a second
            // stale success may start another already-completed restoration with the same public
            // phase, so retain the existing bounded polling deadline rather than assert here.
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
        controller.DiagnosticOperation(0) with { ElapsedMilliseconds = 0 });
}
