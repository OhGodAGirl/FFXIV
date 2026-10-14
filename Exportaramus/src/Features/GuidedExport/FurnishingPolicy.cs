using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public static class FurnishingPolicy
{
    public static bool IsDiscoverable(GuidedFurnishing value, GuidedSource source) =>
        value.Identity != 0 && value.Source == source && value.Valid && value.Allowlisted &&
        value.ExpectedKind && value.Targetable && double.IsFinite(value.Distance) && value.Distance >= 0;

    public static GuidedFurnishing? Discover(ImmutableArray<GuidedFurnishing> objects, GuidedSource source, ulong currentTarget) =>
        objects.Where(x => IsDiscoverable(x, source)).OrderByDescending(x => x.Identity == currentTarget)
            .ThenBy(x => x.Distance).ThenBy(x => x.Identity).FirstOrDefault();

    public static bool IsEligible(GuidedFurnishing value, GuidedSource source) =>
        value.Identity != 0 && value.Source == source && value.Valid && value.Allowlisted &&
        value.ExpectedKind && value.Targetable && double.IsFinite(value.Distance) &&
        double.IsFinite(value.InteractionRange) && value.Distance >= 0 &&
        value.InteractionRange > 0 && value.Distance <= value.InteractionRange;

    public static GuidedFurnishing? Select(ImmutableArray<GuidedFurnishing> objects, GuidedSource source, ulong currentTarget) =>
        objects.Where(x => IsEligible(x, source)).OrderByDescending(x => x.Identity == currentTarget)
            .ThenBy(x => x.Distance).ThenBy(x => x.Identity).FirstOrDefault();
}
