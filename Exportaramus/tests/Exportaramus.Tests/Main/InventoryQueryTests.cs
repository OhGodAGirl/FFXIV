using System.Text.Json;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Main;

public sealed class InventoryQueryTests
{
    [Fact]
    public void ItemTotalsSeparateQualityAndExcludeUnquantifiedStorage()
    {
        var now = DateTimeOffset.UtcNow;
        InventorySnapshot snapshot = new(123, 2, false,
        [new(new(InventoryLocationKind.Inventory), "Inventory", InventoryObservationState.Observed, now, now, [new(55, 12, 2)]),
         new(new(InventoryLocationKind.Retainer, 456), "Alice", InventoryObservationState.Retained, now, now, [new(55, 20, 3)]),
         new(new(InventoryLocationKind.GlamourDresser), "Dresser", InventoryObservationState.Observed, now, now, [new(55, 0, 0)])]);
        using var json = JsonDocument.Parse(InventoryQuery.Item(snapshot, 55));
        var root = json.RootElement;
        Assert.Equal(32UL, root.GetProperty("normalQuantity").GetUInt64());
        Assert.Equal(5UL, root.GetProperty("highQualityQuantity").GetUInt64());
        Assert.True(root.GetProperty("hasUnknownLocations").GetBoolean());
        Assert.True(root.GetProperty("hasRetainedLocations").GetBoolean());
        Assert.Equal("456", root.GetProperty("locations")[1].GetProperty("retainerId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("locations")[2].GetProperty("normalQuantity").ValueKind);
    }
    [Fact]
    public void LogoutDoesNotExposePreviousCharacterAndZeroIdIsInvalid()
    {
        using var json = JsonDocument.Parse(InventoryQuery.Item(null, 55));
        Assert.False(json.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("contentId").ValueKind);
        Assert.Contains("InvalidItemId", InventoryQuery.Item(null, 0));
    }
    [Fact]
    public void CompleteEmptyObservationRemainsDistinctFromUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        InventorySnapshot snapshot = new(123, 1, true,
            [new(new(InventoryLocationKind.Inventory), "Inventory", InventoryObservationState.Observed, now, now, [])]);
        using var json = JsonDocument.Parse(InventoryQuery.Snapshot(snapshot));
        Assert.Equal("Observed", json.RootElement.GetProperty("locations")[0].GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("locations")[0].GetProperty("items").GetArrayLength());
    }
}
