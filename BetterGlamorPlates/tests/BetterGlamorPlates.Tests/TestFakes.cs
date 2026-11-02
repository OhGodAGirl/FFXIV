namespace BetterGlamorPlates.Tests;

using System.Collections.Immutable;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;

internal sealed class FakePlateLibraryStore : IPlateLibraryStore
{
    internal FakePlateLibraryStore(params SavedPlate[] plates)
    {
        State = new PlateLibraryState(plates.ToList(), plates.FirstOrDefault()?.Id);
    }

    internal PlateLibraryState State { get; }

    internal int SaveCount { get; private set; }

    public PlateLibraryState Load() => State;

    public void Save(PlateLibraryState state)
    {
        Assert.Same(State, state);
        SaveCount++;
    }
}

internal sealed class FakeNativeCollectionValidator : INativeCollectionValidator
{
    internal NativeCollectionSnapshot Snapshot { get; set; } = NativeCollectionSnapshot.Empty;

    internal int CaptureCount { get; private set; }

    public NativeCollectionSnapshot Capture(DateTime capturedAtUtc)
    {
        CaptureCount++;
        return Snapshot with { CapturedAtUtc = capturedAtUtc };
    }
}

internal sealed class FakePlateEditor : IPlateEditorMechanism
{
    internal bool Open { get; set; } = true;

    internal PlateCaptureResult CaptureResult { get; set; } =
        new(PlateCaptureOutcome.EditorNotReady, null);

    internal PlateApplyResult ApplyResult { get; set; } = new(0, 0, []);

    internal int CaptureCount { get; private set; }

    internal string? LastCaptureName { get; private set; }

    internal DateTime? LastCaptureTimestamp { get; private set; }

    internal Guid? LastCapturePlateId { get; private set; }

    internal int ApplyCount { get; private set; }

    internal PlateLoadPlan? LastPlan { get; private set; }

    public bool IsOpen => Open;

    public PlateCaptureResult CaptureCurrent(string name, DateTime capturedAtUtc, Guid plateId)
    {
        CaptureCount++;
        LastCaptureName = name;
        LastCaptureTimestamp = capturedAtUtc;
        LastCapturePlateId = plateId;
        return CaptureResult;
    }

    public PlateApplyResult Apply(PlateLoadPlan plan)
    {
        ApplyCount++;
        LastPlan = plan;
        return ApplyResult;
    }
}

internal sealed class FakeItemFacts : IItemFacts
{
    internal Dictionary<uint, EquipSlotMask> Masks { get; } = [];

    internal Dictionary<uint, string> Names { get; } = [];

    internal Dictionary<uint, uint> Icons { get; } = [];
    internal Dictionary<uint, byte> DyeCounts { get; } = [];

    public ItemDisplayFacts? Get(uint itemId)
    {
        if (!Names.TryGetValue(itemId, out var name) &&
            !Masks.ContainsKey(itemId) &&
            !Icons.ContainsKey(itemId))
        {
            return null;
        }

        return new ItemDisplayFacts(
            name ?? $"item {itemId}",
            Icons.GetValueOrDefault(itemId),
            Masks.GetValueOrDefault(itemId))
        { DyeCount = DyeCounts.GetValueOrDefault(itemId) };
    }
}

internal static class TestDomainValues
{
    internal static DateTime Timestamp { get; } =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static EquipSlotMask AllSlots { get; } = new(
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true);

    internal static SavedPlate Plate(string name, uint itemId = 0)
    {
        var plate = PlateLibrary.Create(
            name,
            Timestamp,
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        if (itemId != 0)
            plate.Items[(int)PlateSlot.Body] = new SavedPlateItem { ItemId = itemId };
        return plate;
    }

    internal static CollectionItem Item(
        uint itemId,
        string name,
        CollectionSource source = CollectionSource.Dresser,
        CollectionFreshness freshness = CollectionFreshness.Fresh,
        EquipSlotMask? mask = null)
        => new(
            itemId,
            name,
            itemId,
            source,
            freshness,
            CollectionProviderPolicy.DescribeFreshness(freshness),
            mask ?? AllSlots);

    internal static NativeCollectionItem NativeItem(
        uint itemId,
        CollectionSource source = CollectionSource.Dresser,
        uint? sourceId = null,
        byte stain0Id = 0,
        byte stain1Id = 0)
        => new(itemId, source, sourceId ?? itemId, stain0Id, stain1Id);

    internal static CollectionDataSnapshot SharedSnapshot(
        string providerVersion = "0.3.0.0",
        CollectionSourceStatus inventory = CollectionSourceStatus.Fresh,
        CollectionSourceStatus armoryChest = CollectionSourceStatus.Fresh,
        CollectionSourceStatus chocoboSaddlebag = CollectionSourceStatus.Stale,
        CollectionSourceStatus premiumSaddlebag = CollectionSourceStatus.NeverObserved,
        CollectionSourceStatus retainers = CollectionSourceStatus.Stale,
        CollectionSourceStatus armoire = CollectionSourceStatus.Fresh,
        CollectionSourceStatus glamourDresser = CollectionSourceStatus.Fresh,
        uint[]? armoireItems = null,
        uint[]? dresserItems = null)
    {
        var capturedAtUtc = new DateTimeOffset(Timestamp, TimeSpan.Zero);
        return new CollectionDataSnapshot(
            "CollectorsCompendium",
            providerVersion,
            42,
            7,
            capturedAtUtc,
            Source(inventory, capturedAtUtc, 11, 12),
            Source(armoryChest, capturedAtUtc, 21),
            Source(chocoboSaddlebag, capturedAtUtc, 31),
            Source(premiumSaddlebag, capturedAtUtc),
            Source(retainers, capturedAtUtc, 41),
            Source(armoire, capturedAtUtc, armoireItems ?? [51]),
            Source(glamourDresser, capturedAtUtc, dresserItems ?? [61, 62]));
    }

    private static CollectionSourceSnapshot Source(
        CollectionSourceStatus status,
        DateTimeOffset observedAtUtc,
        params uint[] itemIds)
    {
        var observation = status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale
            ? observedAtUtc
            : (DateTimeOffset?)null;
        var values = status is CollectionSourceStatus.Unavailable or CollectionSourceStatus.NeverObserved
            ? []
            : itemIds.ToImmutableArray();
        return new CollectionSourceSnapshot(status, observation, values, null);
    }
}

internal sealed class FakeSharedCollectionSnapshotSource : ISharedCollectionSnapshotSource
{
    public CollectionDataSnapshot? Latest { get; set; }
}
