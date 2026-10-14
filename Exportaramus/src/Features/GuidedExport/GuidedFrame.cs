using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public sealed record GuidedFrame(
    TimeSpan Now, ulong ContentId, bool LoggedIn, bool Stopping, bool SafeConditions,
    bool RuntimeValidated, ulong CurrentTarget, ImmutableArray<GuidedFurnishing> Furnishings,
    ulong AddonIdentity, bool ExpectedAddonPresent, bool AddonReady, bool ManagerReady,
    GuidedResult CaptureResult = GuidedResult.Pending, GuidedResult SaveResult = GuidedResult.Pending,
    ulong ArmoireMenuIdentity = 0);
