using Exportaramus.ConsumerKit.V1_2;
namespace Exportaramus.ConsumerKit.Tests;

public sealed class ReentrantClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldRefreshCannotReplaceANewerReentrantSnapshot(bool overflow)
    {
        var transport = new FakeTransport();
        await using var client = new CollectionDataIpcClient((_, _) => transport,
            (action, _) => { action(); return Task.CompletedTask; });
        transport.BeforeSnapshotReturns = () =>
        {
            transport.BeforeSnapshotReturns = null;
            transport.Snapshot = ContractFixture.Edit(transport.Snapshot, root => root["contentId"] = "42");
            if (overflow)
                for (var i = 0; i < 17; i++) client.Withdrawn(ContractFixture.Read("snapshot-withdrawn-valid.json"));
            client.TickAsync("42", ContractFixture.Now).GetAwaiter().GetResult();
            Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
            Assert.Equal("42", client.View.ContentId);
            if (!overflow) client.Withdrawn(ContractFixture.Read("snapshot-withdrawn-valid.json"));
        };
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal("42", client.View.ContentId);
    }
}
