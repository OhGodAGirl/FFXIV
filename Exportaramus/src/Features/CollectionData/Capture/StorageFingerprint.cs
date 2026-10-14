namespace Exportaramus.Features.CollectionData.Capture;

public static class StorageFingerprint
{
    public const ulong Initial = 14695981039346656037;
    public static ulong Add(ulong hash, ulong value)
    {
        ulong mixed = hash ^ value;
        return unchecked(mixed * 1099511628211);
    }
    public static ulong AddDresserEntry(ulong hash, uint rawItemId, ReadOnlySpan<bool> unlockedSlots)
    {
        if (unlockedSlots.Length is not (0 or 11))
            throw new ArgumentException("Dresser fingerprints require zero or eleven slot bits.");
        hash = Add(hash, rawItemId);
        foreach (var unlocked in unlockedSlots) hash = Add(hash, unlocked ? 1ul : 0);
        return hash;
    }
}


