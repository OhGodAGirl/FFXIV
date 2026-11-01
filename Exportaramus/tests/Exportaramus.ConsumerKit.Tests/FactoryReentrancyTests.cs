using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class FactoryReentrancyTests
{
    [Fact]
    public async Task FailedOuterBindingClearsAReentrantAcceptedView()
    {
        var transport = new FakeTransport();
        var calls = 0;
        var nestedReady = false;
        ICollectionDataTransport Bind(Action<string> changed, Action<string> withdrawn)
        {
            if (++calls != 1) return transport;
            var constructingClient = Assert.IsType<CollectionDataIpcClient>(changed.Target);
            constructingClient.TickAsync(ContractFixture.Character, ContractFixture.Now).GetAwaiter().GetResult();
            nestedReady = constructingClient.View.Availability == CollectionDataAvailability.Ready;
            throw new InvalidOperationException("Outer subscription setup failed.");
        }
        await using var client = new CollectionDataIpcClient(Bind, (action, _) => { action(); return Task.CompletedTask; });
        Assert.Equal(2, calls);
        Assert.True(nestedReady);
        Assert.Equal(CollectionDataAvailability.Unavailable, client.View.Availability);
        Assert.Equal(CollectionDataValidationCode.ProviderUnavailable, client.View.Failure);
        Assert.Null(client.View.Snapshot);
    }
}
