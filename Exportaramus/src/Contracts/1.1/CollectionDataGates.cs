namespace Exportaramus.ConsumerKit.V1_1;

public static class CollectionDataGates
{
    public const string Prefix = "Exportaramus.CollectionData.V1_1";
    public const string GetCapabilities = Prefix + ".GetCapabilities";
    public const string GetSnapshot = Prefix + ".GetSnapshot";
    public const string SnapshotChanged = Prefix + ".SnapshotChanged";
    public const string SnapshotWithdrawn = Prefix + ".SnapshotWithdrawn";
    public const string ReportOwnershipEvidence = Prefix + ".ReportOwnershipEvidence";
}
