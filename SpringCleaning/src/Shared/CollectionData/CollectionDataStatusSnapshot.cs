using System.Globalization;

namespace SpringCleaning.Shared.CollectionData;

internal sealed record CollectionDataStatusSnapshot(
    CollectionDataFailure LastFailure,
    bool RefreshRequested,
    bool RefreshInProgress,
    string? ProviderInternalName,
    string? ProviderVersion,
    ulong? Revision,
    DateTimeOffset? CapturedAtUtc,
    CollectionDataSourceDiagnostic Inventory,
    CollectionDataSourceDiagnostic ArmoryChest,
    CollectionDataSourceDiagnostic ChocoboSaddlebag,
    CollectionDataSourceDiagnostic PremiumSaddlebag,
    CollectionDataSourceDiagnostic Retainers,
    CollectionDataSourceDiagnostic Armoire,
    CollectionDataSourceDiagnostic GlamourDresser)
{
    internal bool HasSnapshot => Revision.HasValue;

    internal string ToPreviewSummary()
    {
        const string nativeAuthority = "Exportaramus supplies ownership; native UI revalidates every action.";
        if (!HasSnapshot)
        {
            if (RefreshInProgress || RefreshRequested)
                return $"Shared collection data refreshing. {nativeAuthority}";

            return $"Shared collection data unavailable: {FormatFailure(LastFailure)}. {nativeAuthority}";
        }

        var summary = $"Shared collection data: armoire {Armoire.Status}, glamour dresser {GlamourDresser.Status}.";
        if (LastFailure == CollectionDataFailure.None)
            return $"{summary} {nativeAuthority}";

        return $"{summary} {FormatFailure(LastFailure)}; showing the last accepted snapshot. Native UI revalidates every action.";
    }

    internal IReadOnlyList<string> ToReportLines()
    {
        var provider = string.IsNullOrWhiteSpace(ProviderInternalName)
            ? "unavailable"
            : ProviderVersion is null
                ? ProviderInternalName
                : $"{ProviderInternalName} {ProviderVersion}";
        var capture = HasSnapshot && CapturedAtUtc.HasValue
            ? $"revision {Revision!.Value.ToString(CultureInfo.InvariantCulture)} at {CapturedAtUtc.Value:O}"
            : "none";
        return
        [
            $"Shared collection state: requested={RefreshRequested}; refreshing={RefreshInProgress}; last result={FormatFailure(LastFailure)}; snapshot={(HasSnapshot ? "available" : "none")}",
            $"Shared collection provider: {provider}",
            $"Shared collection capture: {capture}",
            Inventory.ToReportLine("Shared inventory"),
            ArmoryChest.ToReportLine("Shared Armoury Chest"),
            ChocoboSaddlebag.ToReportLine("Shared saddlebag"),
            PremiumSaddlebag.ToReportLine("Shared premium saddlebag"),
            Retainers.ToReportLine("Shared retainers"),
            Armoire.ToReportLine("Shared armoire"),
            GlamourDresser.ToReportLine("Shared glamour dresser"),
        ];
    }

    private static string FormatFailure(CollectionDataFailure failure) => failure switch
    {
        CollectionDataFailure.None => "none",
        CollectionDataFailure.NoCharacter => "no active character",
        CollectionDataFailure.ProviderUnavailable => "provider unavailable",
        CollectionDataFailure.Busy => "refresh already running",
        CollectionDataFailure.Disposed => "disposed",
        CollectionDataFailure.EmptyPayload => "empty payload",
        CollectionDataFailure.PayloadTooLarge => "payload too large",
        CollectionDataFailure.MalformedJson => "malformed json",
        CollectionDataFailure.UnsupportedProtocol => "unsupported protocol",
        CollectionDataFailure.InvalidProvider => "invalid provider",
        CollectionDataFailure.WrongCharacter => "wrong character",
        CollectionDataFailure.InvalidRevision => "invalid revision",
        CollectionDataFailure.OutOfOrder => "out of order",
        CollectionDataFailure.InvalidTimestamp => "invalid timestamp",
        CollectionDataFailure.InvalidSources => "invalid sources",
        CollectionDataFailure.StaleCompletion => "obsolete completion",
        _ => "unknown",
    };
}
