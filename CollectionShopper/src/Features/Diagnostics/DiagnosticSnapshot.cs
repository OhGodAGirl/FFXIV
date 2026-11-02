using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CollectionShopper;

internal sealed record DiagnosticSnapshot(
    string PublicName,
    string InternalName,
    string Version,
    int DalamudApiLevel,
    string Channel,
    DateTimeOffset CheckedAt,
    IReadOnlyList<DiagnosticCheck> Checks,
    Guid? LoadedBuildId = null,
    DiagnosticOperationSnapshot? Operation = null,
    IReadOnlyList<CollectionDataOwnershipSourceState>? Sources = null,
    DateTimeOffset? FrameworkObservedAt = null,
    IReadOnlyList<DiagnosticPredicate>? CollectionCompleteness = null);

internal sealed record DiagnosticPredicate(string Name, bool? Passed, string Expected, string Observed);
internal sealed record DiagnosticFailure(DateTimeOffset AtUtc, string Phase, string Reason,
    int ConfirmedActions, IReadOnlyList<DiagnosticPredicate> Checks,
    IReadOnlyList<CollectionDataOwnershipSourceState>? Sources = null);
internal sealed record DiagnosticOperationSnapshot(string Phase, string Status, int ConfirmedActions,
    IReadOnlyList<DiagnosticPredicate> Checks, DiagnosticFailure? LastFailure);

