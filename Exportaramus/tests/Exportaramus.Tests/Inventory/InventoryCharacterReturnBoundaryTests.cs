using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCharacterReturnBoundaryTests
{
    [Fact]
    public async Task ReturningToTheSameCharacterDoesNotPublishItsEarlierPendingCacheLoad()
    {
        var cache = new TwoReadCache();
        var service = new InventoryService(cache);
        try
        {
            service.SetCharacter(1);
            service.Pump();
            await cache.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.SetCharacter(2);
            service.SetCharacter(1);
            var returned = service.GetSnapshot()!;
            Assert.Equal(1UL, returned.ContentId);
            Assert.False(returned.RetainerRosterKnown);
            Assert.All(returned.Locations, location => Assert.False(location.HasObservation));

            // The first result belongs to the abandoned visit to character1. Keep the
            // replacement read pending so it cannot hide an incorrectly published result.
            cache.FirstResult.TrySetResult(InventoryServiceTests.Saved(1));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cache.SecondStarted.Task.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                service.Pump();
                await Task.Delay(1, deadline.Token);
            }

            Assert.Equal(2, cache.ReadCount);
            Assert.Same(returned, service.GetSnapshot());
            Assert.All(service.GetSnapshot()!.Locations, location => Assert.False(location.HasObservation));
            Assert.DoesNotContain(service.GetSnapshot()!.Locations,
                location => location.Key.Kind == InventoryLocationKind.Retainer);
        }
        finally
        {
            cache.FirstResult.TrySetResult(null);
            cache.SecondResult.TrySetResult(null);
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class TwoReadCache : IInventoryCache
    {
        private int reads;
        public int ReadCount => Volatile.Read(ref reads);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<InventorySnapshot?> FirstResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<InventorySnapshot?> SecondResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            Assert.Equal(1UL, contentId);
            if (Interlocked.Increment(ref reads) == 1)
            {
                FirstStarted.TrySetResult();
                return FirstResult.Task;
            }
            SecondStarted.TrySetResult();
            return SecondResult.Task;
        }

        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
