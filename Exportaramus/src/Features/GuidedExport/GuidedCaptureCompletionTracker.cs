using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.GuidedExport;

/// <summary>Associates requested capture/save effects with the next accepted publication, without observing native state.</summary>
public sealed class GuidedCaptureCompletionTracker
{
    private long requestedRevision = -1;
    private GuidedSource source;
    private GuidedEffectKind kind;
    public void Begin(GuidedEffect effect, long currentRevision)
    { requestedRevision = currentRevision; source = effect.Source; kind = effect.Kind; }
    public void Reset() => requestedRevision = -1;
    public GuidedFrame Apply(GuidedFrame frame, AcceptedSnapshotBundle? accepted, string? error)
    {
        if (requestedRevision < 0) return frame;
        var result = error is not null ? GuidedResult.Failed : accepted is not null && accepted.Revision > requestedRevision ? GuidedResult.Succeeded : GuidedResult.Pending;
        if (kind == GuidedEffectKind.Capture && result == GuidedResult.Succeeded)
        {
            var key = source == GuidedSource.Dresser ? "glamourDresser" : "armoire";
            if (accepted!.Data.SourceHealth[key].SourceStatus != SourceStatus.Fresh) result = GuidedResult.Failed;
        }
        return kind == GuidedEffectKind.Capture ? frame with { CaptureResult = result } : frame with { SaveResult = result };
    }
}
