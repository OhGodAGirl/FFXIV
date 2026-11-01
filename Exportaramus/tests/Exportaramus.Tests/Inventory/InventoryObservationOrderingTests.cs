using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryObservationOrderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OlderSuccessfulReadCannotReplaceANewerAcceptedUnavailableState(bool readFailed)
    {
        // Arrange: retain contents after a newer failed read.
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var key = new InventoryLocationKey(InventoryLocationKind.Inventory);
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.True(service.AcceptLocation(1, key, "Inventory", [new(100, 2, 0)], now));
        Assert.True(service.MarkUnavailable(1, key, "Inventory", now.AddMinutes(2), readFailed));
        var before = service.GetSnapshot()!;

        // Act: deliver an older successful read.
        var accepted = service.AcceptLocation(1, key, "Inventory", [], now.AddMinutes(1));

        // Assert: preserve the current publication and retained data.
        Assert.False(accepted);
        Assert.Same(before, service.GetSnapshot());
        var location = Assert.Single(before.Locations, location => location.Key == key);
        Assert.Equal(now.AddMinutes(2), location.AttemptedAtUtc);
        Assert.Equal(now, location.ObservedAtUtc);
        Assert.Equal(readFailed ? InventoryObservationState.ReadFailed : InventoryObservationState.Retained, location.State);
        Assert.Equal(new InventoryItemQuantity(100, 2, 0), Assert.Single(location.Items));
        Assert.True(service.AcceptLocation(1, key, "Inventory", [], now.AddMinutes(3)));
        Assert.Empty(Assert.Single(service.GetSnapshot()!.Locations, location => location.Key == key).Items);
    }
}
