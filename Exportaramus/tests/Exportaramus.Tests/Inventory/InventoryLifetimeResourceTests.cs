using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryLifetimeResourceTests
{
    [Fact]
    public async Task CompletedShutdownDisposesItsOwnedCancellationSource()
    {
        var service = new InventoryService();
        var field = typeof(InventoryService).GetField("lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lifetime = Assert.IsType<CancellationTokenSource>(field.GetValue(service));
        var token = lifetime.Token;
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);

        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(token.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => lifetime.Token);
        Assert.Null(service.GetSnapshot());
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
