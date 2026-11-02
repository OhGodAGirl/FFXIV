namespace SpringCleaning.Shared.CollectionData;

internal interface ICollectionDataTransport : IDisposable
{
    event Action? SnapshotChanged;
    event Action? SnapshotWithdrawn;
    string ReadCapabilities();
    string ReadSnapshot();
    string ReadInventoryCapabilities();
    string ReadInventorySnapshot();
}
