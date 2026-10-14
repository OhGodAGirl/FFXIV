using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.Inventory;
using Exportaramus.Features.Main;
using Exportaramus.Shared.Ui;
using Xunit;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryBoundaryCoverageTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-12T12:00:00Z");
    private static readonly InventoryLocationKey Bag = new(InventoryLocationKind.Inventory);
    private static InventoryLocationSnapshot Location(InventoryLocationKey key, InventoryObservationState state = InventoryObservationState.Observed) =>
        new(key, "Storage", state, state == InventoryObservationState.Unknown ? null : Now, Now, []);

    [Fact]
    public void NamesAndKeysRejectMalformedValues()
    {
        foreach (var name in new string?[] { null, "", new('x', 65), "bad\nname" }) Assert.False(InventoryPolicy.ValidName(name));
        Assert.True(InventoryPolicy.ValidName(new('x', 64)));
        Assert.False(new InventoryLocationKey(InventoryLocationKind.Inventory, 1).IsValid);
        Assert.False(InventoryPolicy.TryNormalize([new(1_000_000, 1, 0)], out _));
        Assert.False(InventoryPolicy.TryNormalize([null!], out _));
        Assert.False(InventoryPolicy.TryNormalize(Enumerable.Repeat(new InventoryItemQuantity(1, 1, 0), 50001), out _));
    }

    [Fact]
    public void RestorationPreservesLiveIdentityNamesFailuresAndAttemptTimes()
    {
        var saved = new InventorySnapshot(1, 1, true, [Location(Bag), Location(new(InventoryLocationKind.Retainer, 1))]);
        var failed = Location(Bag, InventoryObservationState.Unknown) with { Name = "Live name", State = InventoryObservationState.ReadFailed, AttemptedAtUtc = Now.AddMinutes(1) };
        var current = new InventorySnapshot(1, 2, true, [failed]);
        Assert.Same(current, InventoryPolicy.Restore(current, saved with { ContentId = 2 }));
        var restored = InventoryPolicy.Restore(current, saved);
        Assert.Single(restored.Locations);
        Assert.Equal("Live name", restored.Locations[0].Name);
        Assert.Equal(InventoryObservationState.ReadFailed, restored.Locations[0].State);
        Assert.Equal(Now.AddMinutes(1), restored.Locations[0].AttemptedAtUtc);
        current = current with { RetainerRosterKnown = false, Locations = [failed with { State = InventoryObservationState.Unknown, AttemptedAtUtc = null }] };
        restored = InventoryPolicy.Restore(current, saved);
        Assert.Equal(2, restored.Locations.Length);
        Assert.Equal(InventoryObservationState.Retained, restored.Locations[0].State);
        Assert.Equal(Now, restored.Locations[0].AttemptedAtUtc);
        Assert.Equal(InventoryObservationState.Unknown, InventoryPolicy.Restore(current, saved with { Locations = [Location(Bag, InventoryObservationState.Unknown)] }).Locations[0].State);
    }

    [Fact]
    public void SnapshotQueriesKeepUnknownAndUnquantifiedStorageDistinct()
    {
        using var absent = JsonDocument.Parse(InventoryQuery.Snapshot(null));
        Assert.False(absent.RootElement.GetProperty("available").GetBoolean());
        var snapshot = new InventorySnapshot(1, 3, true,
            [Location(Bag) with { Items = [new(100, 2, 3)] },
             Location(new(InventoryLocationKind.Armoire)) with { Items = [new(100, 0, 0)] },
             Location(new(InventoryLocationKind.GlamourDresser)) with { Items = [new(100, 0, 0)] }]);
        using var document = JsonDocument.Parse(InventoryQuery.Snapshot(snapshot));
        Assert.Equal(2u, document.RootElement.GetProperty("locations")[0].GetProperty("items")[0].GetProperty("normalQuantity").GetUInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("locations")[1].GetProperty("items")[0].GetProperty("highQualityQuantity").ValueKind);
        using var item = JsonDocument.Parse(InventoryQuery.Item(snapshot, 100));
        Assert.False(item.RootElement.GetProperty("hasUnknownLocations").GetBoolean());
        using var unknown = JsonDocument.Parse(InventoryQuery.Item(snapshot with { Locations = snapshot.Locations.Add(Location(new(InventoryLocationKind.ArmoryChest), InventoryObservationState.Unknown)) }, 100));
        Assert.True(unknown.RootElement.GetProperty("hasUnknownLocations").GetBoolean());
        Assert.Same(InventoryPresentation.Empty, InventoryPresentation.Prepare(null, null, Now));
        var view = InventoryPresentation.Prepare(snapshot with { RetainerRosterKnown = false }, null, Now);
        Assert.Equal("Unknown", view.RetainerCount);
        Assert.Null(view.ConfirmedOutfits);
    }

    [Fact]
    public async Task LocationRefreshAndFailureBoundariesPreserveLatestObservation()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.True(service.ObserveRoster(1, []));
        Assert.False(service.ObserveRoster(1, []));
        Assert.False(service.MarkUnavailable(1, Bag, "Bag", default, false));
        Assert.True(service.MarkUnavailable(1, Bag, "Renamed", Now, false));
        Assert.False(service.MarkUnavailable(1, Bag, "Renamed", Now.AddMinutes(-1), true));
        Assert.True(service.AcceptLocation(1, Bag, "Renamed", [new(100, 1, 0)], Now));
        Assert.True(service.AcceptLocation(1, Bag, "Changed name", [new(100, 1, 0)], Now.AddSeconds(1)));
        Assert.True(service.AcceptLocation(1, Bag, "Changed name", [new(100, 2, 0)], Now.AddSeconds(2)));
        Assert.True(service.AcceptLocation(1, Bag, "Changed name", [new(100, 2, 0)], Now.AddMinutes(2)));
        Assert.True(service.MarkUnavailable(1, Bag, "Changed name", Now.AddMinutes(3), false));
        Assert.True(service.MarkUnavailable(1, Bag, "Other name", Now.AddMinutes(4), false));
        Assert.Equal(InventoryObservationState.Retained, service.GetSnapshot()!.Locations.Single(x => x.Key == Bag).State);
        Assert.False(service.MarkUnavailable(0, Bag, "Bag", Now, true));
    }

    [Fact]
    public void ApplyingThemeExposesEverySelectedPaletteToken()
    {
        VisualTokens.ApplyTheme(1);
        var palette = ThemePolicy.Resolve(1);
        Assert.Equal(new[] { palette.Window, palette.Panel, palette.Raised, palette.Alternate, palette.Gold, palette.Heading, palette.Icon, palette.Border, palette.Primary, palette.Secondary, palette.Muted, palette.Count, palette.Success, palette.Warning, palette.Error, palette.Shadow },
            new[] { VisualTokens.Window, VisualTokens.Panel, VisualTokens.Raised, VisualTokens.Alternate, VisualTokens.Gold, VisualTokens.Heading, VisualTokens.Icon, VisualTokens.Border, VisualTokens.Primary, VisualTokens.Secondary, VisualTokens.Muted, VisualTokens.Count, VisualTokens.Success, VisualTokens.Warning, VisualTokens.Error, VisualTokens.Shadow });
        VisualTokens.ApplyTheme(null);
    }
}
