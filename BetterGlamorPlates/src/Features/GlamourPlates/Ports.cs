using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal interface INativeCollectionValidator
{
    NativeCollectionSnapshot Capture(DateTime capturedAtUtc);
}

internal interface ISharedCollectionSnapshotSource
{
    CollectionDataSnapshot? Latest { get; }
}

internal sealed class DelegatingSharedCollectionSnapshotSource : ISharedCollectionSnapshotSource
{
    private readonly Func<CollectionDataSnapshot?> getLatest;

    internal DelegatingSharedCollectionSnapshotSource(Func<CollectionDataSnapshot?> getLatest)
    {
        this.getLatest = getLatest;
    }

    public CollectionDataSnapshot? Latest => getLatest();
}

internal interface IItemFacts
{
    ItemDisplayFacts? Get(uint itemId);
}

internal interface IPlateEditorMechanism
{
    bool IsOpen { get; }

    PlateCaptureResult CaptureCurrent(string name, DateTime capturedAtUtc, Guid plateId);

    PlateApplyResult Apply(PlateLoadPlan plan);
}

internal interface IPlateLibraryStore
{
    PlateLibraryState Load();

    void Save(PlateLibraryState state);
}

internal sealed class PlateLibraryState
{
    internal PlateLibraryState(List<SavedPlate> savedPlates, Guid? selectedPlateId)
    {
        SavedPlates = savedPlates;
        SelectedPlateId = selectedPlateId;
    }

    internal List<SavedPlate> SavedPlates { get; }

    internal Guid? SelectedPlateId { get; set; }
}
