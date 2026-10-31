using System.Diagnostics;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

internal static class DiagnosticWorkerTest
{
    public static async Task DrainAsync(DiagnosticsController controller)
    {
        var started = Stopwatch.GetTimestamp();
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            if (controller.OwnedTask.State is not ("Waiting" or "Running")) break;
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3), "The owned diagnostic worker must complete within the fixture deadline.");
            await Task.Delay(1);
        }

        Assert.DoesNotContain(controller.OwnedTask.State, new[] { "Waiting", "Running" });
        controller.Update();
        Assert.Equal("NotStarted", controller.OwnedTask.State);
        Assert.False(controller.View.Running);
    }
}
