using Exportaramus.Features.CollectionData.Capture;

namespace Exportaramus.Tests.Capture;

public sealed class DresserReadinessFingerprintTests
{
    private static ulong Fingerprint(params (uint Id, bool[] Slots)[] entries)
    {
        var hash = StorageFingerprint.Initial;
        foreach (var entry in entries) hash = StorageFingerprint.AddDresserEntry(hash, entry.Id, entry.Slots);
        return hash;
    }

    [Fact]
    public void SameAggregatePhysicalCopyAndOutfitPartitionChangesRequestRecapture()
    {
        // Every state has the same aggregate item 100; physical ownership differs.
        var one = Fingerprint((100, []), (0, []));
        var two = Fingerprint((100, []), (100, []));
        // Outfit 200 has item 100 in slots zero and two: partition differs, union does not.
        var outfitA = Fingerprint((200, [true, false, false, false, false, false, false, false, false, false, false]), (100, []));
        var outfitB = Fingerprint((200, [false, false, true, false, false, false, false, false, false, false, false]), (100, []));
        var policy = new StorageReadinessPolicy();
        bool Poll(ulong hash, double now, bool ready = true) =>
            policy.Poll(true, key => key == "glamourDresser", _ => (ready, hash), now, false);
        Assert.True(Poll(one, 0));
        Assert.False(Poll(one, 1));
        Assert.True(Poll(two, 2));
        Assert.True(Poll(outfitA, 3));
        Assert.True(Poll(outfitB, 4));
        Assert.False(Poll(outfitB, 5));
        Assert.False(Poll(two, 6, false));
        Assert.False(Poll(one, 7, false));
        Assert.True(Poll(one, 8));
    }

    [Fact]
    public void FingerprintPreservesPhysicalIndexAndRawEncodingAndExistingHashSemantics()
    {
        Assert.NotEqual(Fingerprint((100, []), (0, [])), Fingerprint((0, []), (100, [])));
        Assert.NotEqual(Fingerprint((100, [])), Fingerprint((1_000_100, [])));
        bool[] bits = [true, false, true, false, false, false, false, false, false, false, false];
        var expected = StorageFingerprint.Add(StorageFingerprint.Initial, 200);
        foreach (var bit in bits) expected = StorageFingerprint.Add(expected, bit ? 1ul : 0);
        Assert.Equal(expected, Fingerprint((200, bits)));
        Assert.Throws<ArgumentException>(() => StorageFingerprint.AddDresserEntry(0, 200, [true]));
    }
}
