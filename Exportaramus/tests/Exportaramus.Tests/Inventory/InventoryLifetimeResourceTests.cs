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
    [Fact]
    public async Task FaultedRestoreCannotSkipFinalSaveOrCancellationCleanup()
    {
        var cache = new FaultedRestoreCache();
        await using var service = new InventoryService(cache);
        var lifetime = (CancellationTokenSource)typeof(InventoryService)
            .GetField("lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        service.SetCharacter(1);
        Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory",
            [new(100, 1, 0)], DateTimeOffset.UtcNow));
        service.Pump();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(cache.Saved);
        Assert.Equal((ulong)1, cache.Saved.ContentId);
        Assert.Contains(cache.Saved.Locations, location => location.Items.Any(item => item.ItemId == 100));
        Assert.Null(service.GetSnapshot());
        Assert.Throws<ObjectDisposedException>(() => lifetime.Token);
    }

    private sealed class FaultedRestoreCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InventorySnapshot? Saved
        {
            get; private set;
        }
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            Started.SetResult();
            return Task.FromException<InventorySnapshot?>(new IOException("Restore failed."));
        }
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            Saved = snapshot;
            return Task.CompletedTask;
        }
    }
}
