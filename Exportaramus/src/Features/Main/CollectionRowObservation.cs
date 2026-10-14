using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public sealed record CollectionRowObservation(int? Count, bool Stale, int? Delta, RowEvidenceKind Kind, string Status = "", string Detail = "", bool Partial = false);
