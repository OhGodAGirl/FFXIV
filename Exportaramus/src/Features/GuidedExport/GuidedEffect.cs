using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public sealed record GuidedEffect(GuidedEffectKind Kind, GuidedSource Source, ulong TargetIdentity = 0,
    ulong AddonIdentity = 0, long Generation = 0)
{
    public static readonly GuidedEffect None = new(GuidedEffectKind.None, GuidedSource.Dresser);
}
