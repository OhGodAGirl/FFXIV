using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public sealed record MainPresentationInput(long Revision, MainStatus Status, bool HasCharacter, bool HasSnapshot,
    bool ReduceMotion, bool PreparedExportAvailable, int HistoryCount, DateTimeOffset? LastRefresh,
    DateTimeOffset? LatestSnapshot, string Message, string HealthHeadline, UiTone HealthTone,
    ImmutableArray<HealthFact> HealthFacts, ImmutableDictionary<string, CollectionRowObservation> Collections,
    GuidedView Guided);
