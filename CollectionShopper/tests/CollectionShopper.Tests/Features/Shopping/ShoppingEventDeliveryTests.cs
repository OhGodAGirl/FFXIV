namespace CollectionShopper.Tests;

public sealed class ShoppingEventDeliveryTests
{
    [Fact]
    public void DisposedDeliveryDoesNotDrainOrInvokeCallbacks()
    {
        using var queue = new ShoppingEventQueue();
        queue.Enqueue(new ShoppingPurchase(42, 42, 1));

        ShoppingEventDelivery.Dispatch(queue, () => true,
            () => Assert.Fail("Overflow"), _ => Assert.Fail("Offerings"),
            _ => Assert.Fail("Purchase"), () => Assert.Fail("Updated"));

        Assert.Single(queue.TakePending(out var overflow));
        Assert.False(overflow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchKeepsArrivalOrderAndUpdatesAfterDelivery(bool disposeDuringFirstCallback)
    {
        using var queue = new ShoppingEventQueue();
        queue.Enqueue(new ShoppingOfferings(9, [42]));
        queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        var disposed = false;
        var calls = new List<string>();

        ShoppingEventDelivery.Dispatch(queue, () => disposed,
            () => Assert.Fail("Overflow"),
            offering => { Assert.Equal(9, offering.RequestId); calls.Add("offer"); disposed = disposeDuringFirstCallback; },
            purchase => { Assert.Equal(42u, purchase.CatalogId); calls.Add("purchase"); },
            () => calls.Add("update"));

        Assert.Equal(disposeDuringFirstCallback ? ["offer"] : ["offer", "purchase", "update"], calls);
        Assert.Empty(queue.TakePending(out _));
    }

    [Fact]
    public void DisposalDuringLastPurchaseSuppressesUpdate()
    {
        using var queue = new ShoppingEventQueue();
        queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        var disposed = false;
        var delivered = 0;

        ShoppingEventDelivery.Dispatch(queue, () => disposed,
            () => Assert.Fail("Overflow"), _ => Assert.Fail("Offerings"),
            _ => { delivered++; disposed = true; queue.Dispose(); }, () => Assert.Fail("Updated after disposal"));

        Assert.Equal(1, delivered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowFaultPrecedesUpdateAndCanDispose(bool disposeOnFault)
    {
        using var queue = new ShoppingEventQueue();
        for (var index = 0; index <= ShoppingEventQueue.MaximumPendingEvents; index++)
            queue.Enqueue(new ShoppingPurchase(42, 42, 1));
        var disposed = false;
        var calls = new List<string>();

        ShoppingEventDelivery.Dispatch(queue, () => disposed,
            () => { calls.Add("fault"); disposed = disposeOnFault; },
            _ => Assert.Fail("Overflowed offering"), _ => Assert.Fail("Overflowed purchase"),
            () => calls.Add("update"));

        Assert.Equal(disposeOnFault ? ["fault"] : ["fault", "update"], calls);
    }

    [Fact]
    public void CallbackEnqueueIsDeliveredNextFrame()
    {
        using var queue = new ShoppingEventQueue();
        queue.Enqueue(new ShoppingOfferings(9, [42]));
        var purchases = 0;

        ShoppingEventDelivery.Dispatch(queue, () => false, () => Assert.Fail("Overflow"),
            _ => queue.Enqueue(new ShoppingPurchase(42, 42, 1)), _ => purchases++, () => { });
        Assert.Equal(0, purchases);
        ShoppingEventDelivery.Dispatch(queue, () => false, () => Assert.Fail("Overflow"),
            _ => Assert.Fail("Repeated offering"), _ => purchases++, () => { });

        Assert.Equal(1, purchases);
    }
}
