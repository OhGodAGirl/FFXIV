using System.Reflection;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticDisposalPublicationRaceTests
{
    [Fact]
    public void DisposalSuppressesFailureFromAnUpdateAlreadyInProgress() => VerifyDisposalPublication(false);

    [Fact]
    public void DisposalDoesNotReviveTheGenerationInvalidatedByClose() => VerifyDisposalPublication(true);

    private static void VerifyDisposalPublication(bool closeFirst)
    {
        var measurements = new DiagnosticMeasurements();
        var controller = new DiagnosticsController(_ => Task.FromException<DiagnosticReport>(new InvalidOperationException("fixture")), measurements, _ => { });
        var workerField = typeof(DiagnosticsController).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var gate = typeof(DiagnosticMeasurements).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(measurements)!;
        Task? update = null;
        var held = false;
        try
        {
            controller.Dispatch(DiagnosticsIntent.GenerateReport);
            var worker = Assert.IsAssignableFrom<Task>(workerField.GetValue(controller));
            Assert.Throws<InvalidOperationException>(() => worker.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult());
            if (closeFirst) controller.Dispatch(DiagnosticsIntent.Close);

            // Hold only the telemetry lock: Update detaches its completed worker before recording
            // the failure. This schedules disposal before publication without changing any state.
            Monitor.Enter(gate, ref held);
            update = Task.Factory.StartNew(controller.Update, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(SpinWait.SpinUntil(() => workerField.GetValue(controller) is null, TimeSpan.FromSeconds(3)), "Update must detach its completed worker before disposal.");
            controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            var disposedView = controller.View;
            Monitor.Exit(gate);
            held = false;
            update.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();

            Assert.Equal(disposedView, controller.View);
            Assert.False(controller.View.Running);
            Assert.Null(controller.View.Report);
            Assert.Empty(controller.View.Error);
            Assert.Single(measurements.Failures);
        }
        finally
        {
            if (held) Monitor.Exit(gate);
            try { update?.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
            finally { controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        }
    }
}
