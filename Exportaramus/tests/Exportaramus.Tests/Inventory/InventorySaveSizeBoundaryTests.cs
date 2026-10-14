using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventorySaveSizeBoundaryTests : IDisposable
{
    private static readonly string ScratchRoot = Path.Combine(Path.GetTempPath(), "Exportaramus.Tests", "runtime-data");
    private readonly string directory = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AValidSnapshotAtTheExactSerializedByteLimitCanBeSavedAndRestored()
    {
        // Use the compiled production serializer so the boundary is its actual UTF-8 output,
        // not padding added to a different document or a parallel serializer contract.
        var contextType = typeof(InventoryFileCache).Assembly.GetType("Exportaramus.Features.Inventory.InventoryCacheJsonContext", true)!;
        var context = (JsonSerializerContext)contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var metadata = (JsonTypeInfo<InventoryCacheDocument>)context.GetTypeInfo(typeof(InventoryCacheDocument))!;
        var now = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var first = new InventoryLocationSnapshot(new(InventoryLocationKind.Inventory), "A", InventoryObservationState.Observed, now, now, []);
        var second = first with { Key = new(InventoryLocationKind.ArmoryChest) };
        var empty = new InventorySnapshot(1, 1, false, [first, second]);
        byte[] Serialize(InventorySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(new InventoryCacheDocument("1.0", snapshot), metadata);

        var emptySize = Serialize(empty).Length;
        var oneSize = Serialize(empty with { Locations = [first with { Items = [new(100000, 1, 0)] }, second] }).Length;
        var itemWithSeparatorSize = oneSize - emptySize + 1;
        Assert.InRange(itemWithSeparatorSize, 2, 128);
        var itemCount = (InventoryFileCache.MaximumBytes - emptySize + 2) / itemWithSeparatorSize;
        var firstCount = InventoryPolicy.MaximumItemsPerLocation;
        var secondCount = itemCount - firstCount;
        Assert.InRange(secondCount, 1, InventoryPolicy.MaximumItemsPerLocation);
        var padding = InventoryFileCache.MaximumBytes - (emptySize + itemCount * itemWithSeparatorSize - 2);
        Assert.InRange(padding, 0, 2 * (InventoryPolicy.MaximumNameLength - 1));
        var firstPadding = Math.Min(padding, InventoryPolicy.MaximumNameLength - 1);
        var snapshot = empty with
        {
            Locations =
            [
                first with { Name = new string('A', 1 + firstPadding), Items = Items(firstCount) },
                second with { Name = new string('A', 1 + padding - firstPadding), Items = Items(secondCount) },
            ],
        };
        var expected = Serialize(snapshot);
        Assert.Equal(InventoryFileCache.MaximumBytes, expected.Length);
        var cache = new InventoryFileCache(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await cache.SaveAsync(snapshot, timeout.Token).WaitAsync(TimeSpan.FromSeconds(5));

        var actual = await File.ReadAllBytesAsync(cache.CharacterPath(1), timeout.Token);
        Assert.Equal(expected, actual);
        var restored = await cache.LoadAsync(1, timeout.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(restored);
        Assert.Equal(snapshot.ContentId, restored.ContentId);
        Assert.Equal(firstCount, restored.Locations[0].Items.Length);
        Assert.Equal(secondCount, restored.Locations[1].Items.Length);
        Assert.True(snapshot.Locations[0].Items.SequenceEqual(restored.Locations[0].Items));
        Assert.True(snapshot.Locations[1].Items.SequenceEqual(restored.Locations[1].Items));
    }

    private static ImmutableArray<InventoryItemQuantity> Items(int count) =>
        Enumerable.Range(100000, count).Select(id => new InventoryItemQuantity((uint)id, 1, 0)).ToImmutableArray();

    public void Dispose()
    {
        var root = Path.GetFullPath(ScratchRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test directory must remain inside its scratch root.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
