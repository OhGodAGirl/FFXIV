using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class CapabilityFactoryContractTests
{
    [Fact]
    public async Task InitialCapabilitiesUseTheInjectedPreparationFactory()
    {
        var invocationCount = 0;
        string? prepared = null;
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = Path.Combine(AppContext.BaseDirectory, "capability-factory", Guid.NewGuid().ToString("N"));
        var controller = new SnapshotController(new Port(), new(directory), null, prepareCapabilities: work =>
        {
            invocationCount++;
            prepared = work();
            return completion.Task;
        });
        try
        {
            controller.Update(0, "", true, false, 0, SnapshotFixture.Now);
            Assert.Equal(1, invocationCount);
            Assert.False(string.IsNullOrEmpty(prepared));
            Assert.Empty(controller.Publication.GetCapabilitiesV12());

            completion.SetResult(prepared!);
            controller.Update(0, "", true, false, 0, SnapshotFixture.Now);
            Assert.Equal(prepared, controller.Publication.GetCapabilitiesV12());
            Assert.Equal(1, invocationCount);
        }
        finally
        {
            // Release the owned task even if an earlier assertion fails; never invoke a missing delegate.
            completion.TrySetResult(prepared ?? string.Empty);
            controller.Dispose();
            await controller.ShutdownTask;
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong id, long generation, DateTimeOffset now) => throw new InvalidOperationException();
        public bool Advance() => throw new InvalidOperationException();
        public RawCaptureBatch? TakeResult() => throw new InvalidOperationException();
        public void ResetSession() { }
        public void Dispose() { }
    }
}
