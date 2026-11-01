using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Diagnostics;

internal static class DiagnosticWorkerTest
{
    public static async Task DrainAsync(DiagnosticsController controller)
    {
        while (controller.OwnedTask.State is "Waiting" or "Running")
            await Task.Delay(1);

        Assert.DoesNotContain(controller.OwnedTask.State, new[] { "Waiting", "Running" });
        controller.Update();
        Assert.Equal("NotStarted", controller.OwnedTask.State);
        Assert.False(controller.View.Running);
    }
}
