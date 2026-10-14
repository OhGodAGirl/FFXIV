using System.Text.Json;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRosterExecutionBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnavailableSnapshotDoesNotClaimAKnownRetainerRosterOnTheWire()
    {
        using var document = JsonDocument.Parse(InventoryQuery.Snapshot(null));
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("contentId").ValueKind);
        Assert.Equal(0, root.GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.False, root.GetProperty("retainerRosterKnown").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("locations").ValueKind);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("readFailed")]
    [InlineData("unavailable")]
    public async Task AKnownRosterDoesNotAuthorizeAnUnlistedRetainer(string observation)
    {
        var service = new InventoryService();
        try
        {
            var known = new InventoryLocationKey(InventoryLocationKind.Retainer, 11);
            var unlisted = new InventoryLocationKey(InventoryLocationKind.Retainer, 22);
            service.SetCharacter(1);
            Assert.True(service.ObserveRoster(1, [new(11, "Known retainer")]));
            Assert.True(service.AcceptLocation(1, known, "Known retainer", [new(100, 2, 1)], Now));
            var before = service.GetSnapshot()!;
            Assert.True(before.RetainerRosterKnown);
            Assert.DoesNotContain(before.Locations, location => location.Key == unlisted);
            var wireBefore = InventoryQuery.Snapshot(before);

            var accepted = observation switch
            {
                "capture" => service.AcceptLocation(1, unlisted, "Unlisted retainer", [new(200, 1, 0)], Now.AddSeconds(1)),
                "readFailed" => service.MarkUnavailable(1, unlisted, "Unlisted retainer", Now.AddSeconds(1), true),
                "unavailable" => service.MarkUnavailable(1, unlisted, "Unlisted retainer", Now.AddSeconds(1), false),
                _ => throw new ArgumentOutOfRangeException(nameof(observation)),
            };

            Assert.False(accepted);
            Assert.Same(before, service.GetSnapshot());
            Assert.Equal(wireBefore, InventoryQuery.Snapshot(service.GetSnapshot()));
            Assert.Equal(new(100, 2, 1), Assert.Single(before.Locations.Single(location => location.Key == known).Items));
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
