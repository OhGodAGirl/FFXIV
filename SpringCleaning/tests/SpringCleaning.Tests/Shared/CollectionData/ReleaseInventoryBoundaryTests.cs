using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Tests;

public sealed class ReleaseInventoryBoundaryTests
{
    [Theory]
    [InlineData("inventory", "Inventory")]
    [InlineData("armoryChest", "ArmoryChest")]
    public void AnotherObservedLocationCannotAuthorizeTheRequestedSource(string source, string kind)
    {
        var snapshot = ObservedSnapshot();
        var failed = snapshot with
        {
            Locations = snapshot.Locations.Select(location => location.Kind == kind
                ? location with { State = "ReadFailed" } : location).ToArray(),
        };
        Assert.False(InventoryPresenceAuthority.IsReady(failed, "42", source));

        var missing = snapshot with
        {
            Locations = snapshot.Locations.Where(location => location.Kind != kind).ToArray(),
        };
        Assert.False(InventoryPresenceAuthority.IsReady(missing, "42", source));
        Assert.True(InventoryPresenceAuthority.IsReady(snapshot, "42", source));
    }

    [Fact]
    public void EquippedItemsBelongOnlyToTheArmouryProjection()
    {
        var snapshot = ObservedSnapshot();
        uint[] canonical = [100, 200, 300, 999];

        Assert.Equal(new uint[] { 100 }, InventoryPresenceAuthority.CurrentIds(snapshot, "inventory", canonical));
        Assert.Equal(new uint[] { 200, 300 }, InventoryPresenceAuthority.CurrentIds(snapshot, "armoryChest", canonical));
    }

    private static InventorySnapshot ObservedSnapshot() => new("42", 1, false,
    [
        new("Inventory", "0", "Observed", "2026-09-15T00:00:00Z", true, [new(100, 1, 0)]),
        new("ArmoryChest", "0", "Observed", "2026-09-15T00:00:00Z", true, [new(200, 1, 0)]),
        new("Equipped", "0", "Observed", "2026-09-15T00:00:00Z", true, [new(300, 1, 0), new(400, 1, 0)]),
    ], "observed-fixture");
}
