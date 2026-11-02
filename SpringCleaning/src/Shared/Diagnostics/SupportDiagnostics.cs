using System.Text.Json;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class SupportDiagnostics
{
    private readonly Dictionary<string, Check> checks = new();
    private Failure? lastFailure;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal sealed record Check(string Name, string State, long? Expected, long? Observed, DateTimeOffset CheckedAtUtc);
    internal sealed record Failure(DateTimeOffset CapturedAtUtc, string Phase, string Reason,
        int ConfirmedActions, Check[] Checks, object Sources);

    internal void RetainOperationFailure(string phase, int confirmed, CollectionDataStatusSnapshot sources)
        => lastFailure = new Failure(DateTimeOffset.UtcNow, phase, "OperationStopped", confirmed, checks.Values.ToArray(), Sources(sources));

    internal bool Compare(string name, long expected, long? observed)
    {
        if (checks.Count >= 32 && !checks.ContainsKey(name))
        {
            // Stryker disable once Linq: Count >= 32 proves nonempty; FirstOrDefault returns the identical key.
            checks.Remove(checks.Keys.First());
        }
        checks[name] = new Check(name, observed is null ? "Not Available" : observed == expected ? "Pass" : "Failure",
            expected, observed, DateTimeOffset.UtcNow);
        return observed == expected;
    }

    internal void RetainFailure(MigrationDiagnosticSnapshot snapshot, CollectionDataStatusSnapshot sources, string reason)
    {
        // Free-form status, item names, provider strings and activity logs never enter the report.
        var category = reason.Contains("Prism", StringComparison.OrdinalIgnoreCase) ? "GlamourPrismsUnavailable"
            : reason.Contains("confirm", StringComparison.OrdinalIgnoreCase) ? "ConfirmationRejectedOrTimedOut"
            : reason.Contains("changed", StringComparison.OrdinalIgnoreCase) ? "EvidenceChanged"
            : "OperationStopped";
        lastFailure = new Failure(DateTimeOffset.UtcNow, snapshot.Phase.ToString(), category,
            snapshot.StoredCount, checks.Values.ToArray(), Sources(sources));
    }

    internal string Serialize(MigrationDiagnosticSnapshot snapshot, CollectionDataStatusSnapshot sources, Guid loadedModuleId)
        => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTimeOffset.UtcNow,
            plugin = "SpringCleaning",
            version = Version.TryParse(snapshot.Build, out var version) ? version.ToString() : "unknown",
            loadedAssemblyMvid = loadedModuleId,
            current = new
            {
                mode = snapshot.Mode.ToString(),
                phase = snapshot.Phase.ToString(),
                confirmedStorageActions = snapshot.StoredCount,
                snapshot.PendingItems,
                snapshot.PendingOutfits,
                snapshot.PrismsHeld,
                snapshot.PrismCountReady,
                snapshot.DresserLoaded,
                snapshot.CabinetLoaded,
                snapshot.CandidateListReady,
                snapshot.DresserOpen,
                snapshot.ArmoireOpen,
                snapshot.OutfitConfirmationOpen,
                snapshot.ItemConfirmationOpen,
                checks = checks.Values.ToArray(),
                sources = Sources(sources)
            },
            lastFailure,
            unavailableChecks = "Uninstrumented native comparisons are not inferred from window visibility.",
        }, JsonOptions);

    private static object Sources(CollectionDataStatusSnapshot sources) => new
    {
        available = sources.HasSnapshot,
        lastFailure = sources.LastFailure.ToString(),
        sources.RefreshInProgress,
        sources.CapturedAtUtc,
        inventory = Source(sources.Inventory),
        armouryChest = Source(sources.ArmoryChest),
        armoire = Source(sources.Armoire),
        glamourDresser = Source(sources.GlamourDresser),
    };

    private static object Source(CollectionDataSourceDiagnostic source) => new
    {
        status = source.Status.ToString(),
        source.ObservedAtUtc,
        ageSeconds = source.ObservedAtUtc is { } time ? (double?)Math.Max(0, (DateTimeOffset.UtcNow - time).TotalSeconds) : null,
    };
}
