using Exportaramus.Bootstrap;

namespace Exportaramus.Tests.Snapshots;

public sealed class ReentrantConfigurationRequestTests
{
    [Fact]
    public async Task SynchronousFailurePreservesANewerRequestMadeDuringTheWrite()
    {
        var writes = new List<bool>();
        ConfigurationWriteCoordinator? coordinator = null;
        coordinator = new(value =>
        {
            writes.Add(value);
            if (writes.Count == 1)
            {
                coordinator!.Request(false);
                throw new IOException("The starting write failed.");
            }
            return Task.CompletedTask;
        });
        coordinator.Request(true);
        Assert.Throws<IOException>(() => coordinator.Update());
        await coordinator.DisposeAsync();
        Assert.Equal([true, false], writes);
    }
}
