namespace Exportaramus.Features.CollectionData.Publication;

public sealed class CollectionDataPublicationStore
{
    private AcceptedSnapshotBundle? current;
    private sealed record CapabilitySet(string Legacy, string V11, string V12);
    private CapabilitySet capabilities = new(string.Empty, string.Empty, string.Empty);
    public AcceptedSnapshotBundle? Current => Volatile.Read(ref current);
    public string GetSnapshot() => Current?.CompactJson ?? string.Empty;
    public string GetSnapshotV11() => Current?.CompactJsonV11 ?? string.Empty;
    public string GetSnapshotV12() => Current?.CompactJsonV12 ?? string.Empty;
    public string GetCapabilities() => Volatile.Read(ref capabilities).Legacy;
    public string GetCapabilitiesV11() => Volatile.Read(ref capabilities).V11;
    public string GetCapabilitiesV12() => Volatile.Read(ref capabilities).V12;
    public void SetCapabilities(string json, string? canonical = null, string? v12 = null) =>
        Volatile.Write(ref capabilities, new(json, canonical ?? string.Empty, v12 ?? string.Empty));
    public void Accept(AcceptedSnapshotBundle bundle) => Volatile.Write(ref current, bundle);
    public void Withdraw()
    {
        if (Current is { } previous) SetCapabilities(previous.UnavailableReportsCapabilitiesJson, previous.UnavailableReportsCapabilitiesJsonV11, previous.UnavailableReportsCapabilitiesJsonV12);
        Volatile.Write(ref current, null);
    }
}
