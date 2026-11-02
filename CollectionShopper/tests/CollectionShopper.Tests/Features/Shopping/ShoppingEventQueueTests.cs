namespace CollectionShopper.Tests;

/// <summary>Verifies the bounded packet handoff independently of native event-thread affinity.</summary>
public sealed class ShoppingEventQueueTests
{
    /// <summary>Offerings are copied before crossing threads and preserve FIFO order with purchase packets.</summary>
    [Fact]
    public void QueueCopiesOfferingsAndPreservesMixedPacketOrder()
    {
        using var queue = new ShoppingEventQueue();
        uint[] ids = [42, 43];
        queue.Enqueue(new ShoppingOfferings(7, ids));
        queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        queue.Enqueue(new ShoppingOfferings(8, [44]));
        ids[0] = 99;
        var events = queue.TakePending(out var overflow);
        Assert.False(overflow);
        Assert.Equal(3, events.Length);
        Assert.Equal(new uint[] { 42, 43 }, events[0].Offerings!.Value.ItemIds);
        Assert.Null(events[0].Purchase);
        Assert.Equal(new ShoppingPurchase(42, 42, 1), events[1].Purchase);
        Assert.Null(events[1].Offerings);
        Assert.Equal(8, events[2].Offerings!.Value.RequestId);
        Assert.Empty(queue.TakePending(out overflow));
        Assert.False(overflow);
    }

    /// <summary>The exact bound is accepted; overflow discards the batch and reports a fault exactly once.</summary>
    [Fact]
    public void OverflowIsExplicitBoundedAndRecoverableAfterDrain()
    {
        using var queue = new ShoppingEventQueue();
        for (uint index = 1; index <= ShoppingEventQueue.MaximumPendingEvents; index++)
            queue.Enqueue(new ShoppingPurchase(index, index, 1));
        var exact = queue.TakePending(out var overflow);
        Assert.False(overflow);
        Assert.Equal(ShoppingEventQueue.MaximumPendingEvents, exact.Length);
        for (uint index = 1; index <= ShoppingEventQueue.MaximumPendingEvents + 2; index++)
            queue.Enqueue(new ShoppingPurchase(index, index, 1));
        Assert.Empty(queue.TakePending(out overflow));
        Assert.True(overflow);
        Assert.Empty(queue.TakePending(out overflow));
        Assert.False(overflow);
        queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        Assert.Single(queue.TakePending(out overflow));
        Assert.False(overflow);
    }

    /// <summary>Disposal clears pending data and overflow and rejects all later packet kinds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeClearsAndRejectsFuturePackets(bool overflow)
    {
        var queue = new ShoppingEventQueue();
        for (var index = 0; index <= (overflow ? ShoppingEventQueue.MaximumPendingEvents : 0); index++)
            queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        queue.Dispose();
        queue.Dispose();
        queue.Enqueue(new ShoppingPurchase(43, 43, 1));
        queue.Enqueue(new ShoppingOfferings(7, [43]));
        Assert.Empty(queue.TakePending(out var fault));
        Assert.False(fault);
    }

    /// <summary>Background producers publish ordinary data that becomes visible only at the consumer handoff.</summary>
    [Fact]
    public async Task BackgroundProducerPublishesOneCompleteOrderedBatch()
    {
        using var queue = new ShoppingEventQueue();
        await Task.Run(() =>
        {
            for (uint itemId = 1; itemId <= 32; itemId++)
                queue.Enqueue(new ShoppingPurchase(itemId, itemId, 1));
        });
        var batch = queue.TakePending(out var overflow);
        Assert.False(overflow);
        Assert.Equal(Enumerable.Range(1, 32).Select(value => (uint)value),
            batch.Select(value => value.Purchase!.Value.BaseItemId));
    }
}
