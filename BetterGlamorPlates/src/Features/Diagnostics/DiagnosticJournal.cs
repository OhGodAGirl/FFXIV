using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Features.Diagnostics;

internal enum DiagnosticPhase { Idle, LoadValidation, LoadApply, Preview, ExamineSave, Capture, LocalOperation }
internal enum DiagnosticStop { None, Rejected, Exception, Cancelled, CharacterChanged, Completed }
internal enum DiagnosticStatus { Pass, Warning, Failure, Waiting, NotAvailable }
internal enum DiagnosticKey
{
    LoggedIn, EditorVisible, AgentPresent, AgentCurrent, DataPresent, AgentActive, AddonReady, PlateIndex,
    SameData, SamePlate, PlanExecutable, ActivePlate, DresserLoaded, ArmoireLoaded, SlotCompatible,
    ExactSource, ItemId, Source, SourceIndex, Dye0, Dye1, PendingDye, PreviewInitialized, PreviewState,
    SlotCount, UniqueSlots, NativePlateUnchanged, ExamineVisible, FetchState, PlayerIdentity,
    RequestedIdentity, ExpectedIdentity, ExpectedName, EquipmentMatches, OutfitCreated, LocalOwner,
    PlayerOnly, OperationCompleted, PendingDye0, PendingDye1, InventoryDye0, InventoryDye1, ItemDye0, ItemDye1,
    ExamineInventoryLoaded, GlamourId, LocalDyeTarget, DyeChannelSupported, DyeAvailable, DyeAllocation,
}

// Only enumerated labels and numeric values cross this boundary. Never accept exception text,
// player names, provider strings, paths, pointers, or arbitrary diagnostic messages.
internal sealed record DiagnosticCheck(DiagnosticKey Key, DiagnosticStatus Status, long? Expected, long? Observed, int? Slot);
internal sealed record DiagnosticOperation(DateTimeOffset ObservedAtUtc, DiagnosticPhase Phase, DiagnosticStop Stop,
    int ConfirmedActions, ImmutableArray<DiagnosticCheck> Checks)
{
    public DiagnosticContext? Context { get; init; }
}
internal sealed record DiagnosticSource(CollectionSourceStatus Status, DateTimeOffset? ObservedAtUtc, bool? Complete);
internal sealed record DiagnosticContext(DateTimeOffset ObservedAtUtc, bool EditorVisible, bool ExamineVisible,
    bool ProviderSnapshotAvailable, CollectionDataFailure ProviderFailure, DateTimeOffset? SnapshotAtUtc,
    DiagnosticSource Dresser, DiagnosticSource Armoire)
{
    public ImmutableArray<DiagnosticCheck> ExamineChecks { get; init; } = [];
    public bool NativeDyeWindowOpen { get; init; }
}
internal sealed record DiagnosticReport(int SchemaVersion, DateTimeOffset CapturedAtUtc, string Plugin,
    string Version, Guid LoadedModuleVersionId, int DalamudApi, DiagnosticContext? Context,
    DiagnosticOperation Current, DiagnosticOperation? LastFailure);

internal sealed class DiagnosticJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    internal const int MaximumChecks = 64;
    private readonly List<DiagnosticCheck> checks = [];
    internal DiagnosticOperation Current { get; private set; } = new(DateTimeOffset.MinValue,
        DiagnosticPhase.Idle, DiagnosticStop.None, 0, []);
    internal DiagnosticOperation? LastFailure { get; private set; }
    internal DiagnosticContext? Context { get; set; }

    internal void Begin(DiagnosticPhase phase, DateTimeOffset now)
    {
        checks.Clear();
        Current = new(now, phase, DiagnosticStop.None, 0, []);
    }

    internal bool Check(DiagnosticKey key, long expected, long? observed, int? slot = null)
    {
        var passed = observed == expected;
        var check = new DiagnosticCheck(key, observed is null ? DiagnosticStatus.NotAvailable
            : passed ? DiagnosticStatus.Pass : DiagnosticStatus.Failure, expected, observed, slot);
        // Keep the last bounded set so a late failure cannot disappear behind successful checks.
        if (checks.Count == MaximumChecks) checks.RemoveAt(0);
        checks.Add(check);
        Current = Current with { Checks = checks.ToImmutableArray() };
        return passed;
    }

    internal void Confirm() => Current = Current with { ConfirmedActions = Math.Min(100, Current.ConfirmedActions + 1) };

    internal void Finish(DiagnosticStop stop, DateTimeOffset now)
    {
        // An outer exception handler must not replace evidence already captured before native cleanup.
        if (Current.Stop is DiagnosticStop.Rejected or DiagnosticStop.Exception or DiagnosticStop.CharacterChanged or DiagnosticStop.Cancelled)
            return;
        Current = Current with { Stop = stop, ObservedAtUtc = now.ToUniversalTime(), Context = BoundContext(Context) };
        if (stop is not (DiagnosticStop.Completed or DiagnosticStop.None)) LastFailure = Current;
    }

    internal static DiagnosticSource Source(CollectionSourceSnapshot? source)
        => new(source?.Status ?? CollectionSourceStatus.NeverObserved, source?.ObservedAtUtc,
            source?.Status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale ? true : null);

    internal string ToJson(DateTimeOffset now, DiagnosticContext? context)
    {
        var assembly = typeof(DiagnosticJournal).Assembly;
        return JsonSerializer.Serialize(new DiagnosticReport(1, now.ToUniversalTime(), "BetterGlamorPlates",
            assembly.GetName().Version!.ToString(4), assembly.ManifestModule.ModuleVersionId,
            15, BoundContext(context), Current, LastFailure), JsonOptions);
    }

    private static DiagnosticContext? BoundContext(DiagnosticContext? context)
        => context is null ? null : context with
        {
            ExamineChecks = context.ExamineChecks.IsDefault ? [] : context.ExamineChecks.Take(16).ToImmutableArray(),
        };
}
