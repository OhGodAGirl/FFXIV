namespace Exportaramus.ConsumerKit.V1;

public static class CollectionDataGates
{
    public const string Prefix = "Exportaramus.CollectionData";
    public const string GetCapabilities = Prefix + ".GetCapabilities";
    public const string GetSnapshot = Prefix + ".GetSnapshot";
    public const string SnapshotChanged = Prefix + ".SnapshotChanged";
    public const string SnapshotWithdrawn = Prefix + ".SnapshotWithdrawn";
    public const string ReportOwnershipEvidence = Prefix + ".ReportOwnershipEvidence";
}
