using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public sealed record GuidedFurnishing(ulong Identity, GuidedSource Source, bool Valid, bool Allowlisted,
    bool ExpectedKind, bool Targetable, double Distance, double InteractionRange);

