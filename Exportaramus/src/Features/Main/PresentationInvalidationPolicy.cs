using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

/// <summary>Updates time labels once per second and operation changes immediately. Idle frames allocate
/// nothing.</summary>
public sealed class PresentationInvalidationPolicy
{
    private long second = -1;
    private long revision = -1;
    private GuidedState guidedState;
    public bool ShouldRefresh(double monotonicSeconds, long currentRevision, bool hasIntent, GuidedState currentGuidedState)
    {
        if ((long)monotonicSeconds == second && currentRevision == revision && !hasIntent && currentGuidedState == guidedState) return false;
        second = (long)monotonicSeconds;
        revision = currentRevision;
        guidedState = currentGuidedState;
        return true;
    }
}
