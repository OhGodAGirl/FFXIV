namespace Exportaramus.Features.CollectionData.Publication;

public sealed class PreparedSnapshotPublication
{
    public AcceptedSnapshotBundle Bundle { get; }
    public string? Reports { get; }
    public bool Changed { get; }
    public bool Manual { get; }

    internal PreparedSnapshotPublication(AcceptedSnapshotBundle bundle, string? reports, bool changed, bool manual)
    {
        Bundle = bundle;
        Reports = reports;
        Changed = changed;
        Manual = manual;
    }
}
