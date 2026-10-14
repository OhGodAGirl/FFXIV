namespace Exportaramus.Features.GuidedExport;

/// <summary>One bounded transition per framework tick; effects are revalidated by the adapter at execution.</summary>
public sealed class GuidedExportController
{
    public static readonly TimeSpan AppearanceTimeout = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ClosureTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);
    private ulong contentId;
    private ulong targetIdentity;
    private ulong capturedAddon;
    private TimeSpan enteredAt;
    private TimeSpan retryAt;
    private TimeSpan? appearedAt;
    private int dresserAttempts;
    private int armoireAttempts;
    private bool captureRequested;
    private bool saveRequested;
    private bool dresserCompleted;
    private bool armoireCompleted;
    private bool waitingForManualClose;
    private bool menuRequested;
    private GuidedSource source;
    private string prompt = "";
    public GuidedState State { get; private set; }
    public long Generation { get; private set; }
    public GuidedSource CurrentSource => source;
    public bool OwnsWindowEdges => IsActive(State);
    public GuidedView View => new(State, Label(State), prompt, IsActive(State),
        State is GuidedState.Paused or GuidedState.PausedWithPartial,
        dresserCompleted && !armoireCompleted, dresserCompleted, armoireCompleted);

    public GuidedEffect Update(GuidedFrame frame, GuidedCommand command = GuidedCommand.None)
    {
        if (command == GuidedCommand.Start && !IsActive(State))
        {
            if (!frame.LoggedIn || frame.ContentId == 0 || frame.Stopping) return GuidedEffect.None;
            contentId = frame.ContentId;
            Generation++;
            dresserAttempts = armoireAttempts = 0;
            dresserCompleted = armoireCompleted = captureRequested = saveRequested = waitingForManualClose = false;
            menuRequested = false;
            source = GuidedSource.Dresser;
            retryAt = frame.Now;
            Transition(GuidedState.NeedDresser, frame.Now);
            return GuidedEffect.None;
        }

        if (!IsActive(State)) return GuidedEffect.None;
        if (command == GuidedCommand.Cancel || frame.Stopping || !frame.LoggedIn || frame.ContentId != contentId)
        {
            Generation++;
            Transition(GuidedState.Cancelled, frame.Now);
            return GuidedEffect.None;
        }

        if (command == GuidedCommand.SavePartial && dresserCompleted && !armoireCompleted)
        {
            Transition(GuidedState.SavePartialSnapshot, frame.Now);
            return GuidedEffect.None;
        }

        if (State is GuidedState.Paused or GuidedState.PausedWithPartial)
        {
            // SavePartial was handled above for the same completed-dresser/incomplete-armoire invariant.
            if (command == GuidedCommand.Continue || frame.SafeConditions && frame.RuntimeValidated && frame.AddonReady && frame.ManagerReady)
            {
                if (waitingForManualClose)
                {
                    if (!frame.AddonReady || frame.AddonIdentity != capturedAddon)
                    {
                        waitingForManualClose = false;
                        AdvanceAfterClose(frame.Now);
                    }
                }
                else Transition(NeedState, frame.Now);
            }
            return GuidedEffect.None;
        }

        if (State is GuidedState.SaveFullSnapshot or GuidedState.SavePartialSnapshot)
        {
            if (!saveRequested)
            {
                saveRequested = true;
                return Effect(State == GuidedState.SaveFullSnapshot ? GuidedEffectKind.SaveFull : GuidedEffectKind.SavePartial);
            }
            if (frame.SaveResult != GuidedResult.Pending)
                Transition(frame.SaveResult == GuidedResult.Succeeded ? GuidedState.Completed : GuidedState.Failed, frame.Now);
            return GuidedEffect.None;
        }

        if (!frame.RuntimeValidated || !frame.SafeConditions)
        {
            prompt = !frame.RuntimeValidated ? "Waiting for Guided Capture compatibility. Cancel and check the game build if this persists." : "Waiting until it is safe to continue. Capture resumes automatically.";
            return GuidedEffect.None;
        }

        if (State is GuidedState.NeedDresser or GuidedState.NeedArmoire)
        {
            if (frame.Now < retryAt) return GuidedEffect.None;
            if (frame.ExpectedAddonPresent)
            {
                appearedAt = frame.Now;
                Transition(WaitReadyState, frame.Now);
                return GuidedEffect.None;
            }
            var furnishing = FurnishingPolicy.Discover(frame.Furnishings, source, frame.CurrentTarget);
            if (furnishing is null)
            {
                prompt = $"Step {(source == GuidedSource.Dresser ? 1 : 2)} of 2: Move near {(source == GuidedSource.Dresser ? "a Glamour Dresser" : "an Armoire")}. It will open automatically when you are in range.";
                return GuidedEffect.None;
            }
            if (!FurnishingPolicy.IsEligible(furnishing, source))
            {
                targetIdentity = furnishing.Identity;
                prompt = $"Move closer to the targeted {(source == GuidedSource.Dresser ? "Glamour Dresser" : "Armoire")} ({furnishing.Distance:F1} yalms). Movement is left to you.";
                return frame.CurrentTarget == targetIdentity ? GuidedEffect.None : Effect(GuidedEffectKind.SelectTarget);
            }
            if (Attempts >= 8)
            {
                prompt = $"Automatic interaction paused; open the {(source == GuidedSource.Dresser ? "Glamour Dresser" : "Armoire")} manually. Capture will continue automatically.";
                return GuidedEffect.None;
            }
            targetIdentity = furnishing.Identity;
            Transition(InteractionState, frame.Now);
            return frame.CurrentTarget == targetIdentity ? GuidedEffect.None : Effect(GuidedEffectKind.SelectTarget);
        }

        if (State is GuidedState.RequestDresserInteraction or GuidedState.RequestArmoireInteraction)
        {
            if (frame.CurrentTarget != targetIdentity || !frame.Furnishings.Any(x => x.Identity == targetIdentity && FurnishingPolicy.IsEligible(x, source)) || frame.ExpectedAddonPresent)
            {
                Transition(NeedState, frame.Now);
                return GuidedEffect.None;
            }
            if (source == GuidedSource.Dresser) dresserAttempts++; else armoireAttempts++;
            appearedAt = null;
            menuRequested = false;
            Transition(WaitReadyState, frame.Now);
            return Effect(GuidedEffectKind.Interact);
        }

        if (State is GuidedState.WaitForDresserReady or GuidedState.WaitForArmoireReady)
        {
            if (source == GuidedSource.Armoire && !frame.ExpectedAddonPresent && frame.ArmoireMenuIdentity != 0 && !menuRequested)
            {
                menuRequested = true;
                return new(GuidedEffectKind.SelectArmoireMenu, source, targetIdentity, frame.ArmoireMenuIdentity, Generation);
            }
            if (frame.ExpectedAddonPresent)
            {
                appearedAt ??= frame.Now;
                if (frame.AddonReady && frame.ManagerReady && frame.AddonIdentity != 0)
                {
                    capturedAddon = frame.AddonIdentity;
                    captureRequested = false;
                    Transition(CaptureState, frame.Now);
                }
                else if (frame.Now - appearedAt.Value >= ReadinessTimeout)
                    Pause("Reopen the requested window manually, then select Continue.", frame.Now);
            }
            else if (appearedAt.HasValue)
                Pause("Reopen the requested window manually, then select Continue.", frame.Now);
            else if (frame.Now - enteredAt >= AppearanceTimeout)
            {
                if (Attempts < 8)
                {
                    retryAt = frame.Now + RetryDelay;
                    Transition(NeedState, frame.Now);
                }
                else { Transition(NeedState, frame.Now); prompt = "Automatic interaction paused. Open the window manually to continue automatically."; }
            }
            return GuidedEffect.None;
        }

        if (State is GuidedState.CaptureDresser or GuidedState.CaptureArmoire)
        {
            if (!ExactReady(frame))
            {
                Pause("Reopen the requested window manually, then select Continue.", frame.Now);
                return GuidedEffect.None;
            }
            if (!captureRequested)
            {
                captureRequested = true;
                return Effect(GuidedEffectKind.Capture);
            }
            if (frame.CaptureResult == GuidedResult.Succeeded)
            {
                if (source == GuidedSource.Dresser) dresserCompleted = true; else armoireCompleted = true;
                Transition(CloseState, frame.Now);
            }
            else if (frame.CaptureResult == GuidedResult.Failed)
                Pause("Capture failed. Reopen the requested window manually, then select Continue.", frame.Now);
            return GuidedEffect.None;
        }

        if (State is GuidedState.RequestDresserClose or GuidedState.RequestArmoireClose)
        {
            if (!ExactReady(frame))
            {
                waitingForManualClose = true;
                Pause("Close the captured window manually, then select Continue.", frame.Now);
                return GuidedEffect.None;
            }
            Transition(WaitClosedState, frame.Now);
            return Effect(GuidedEffectKind.CloseAddon);
        }

        if (!frame.AddonReady || frame.AddonIdentity != capturedAddon)
            AdvanceAfterClose(frame.Now);
        else if (frame.Now - enteredAt >= ClosureTimeout)
        {
            waitingForManualClose = true;
            Pause("Close the captured window manually, then select Continue.", frame.Now);
        }
        return GuidedEffect.None;
    }

    private bool ExactReady(GuidedFrame frame) => frame.ExpectedAddonPresent && frame.AddonReady && frame.ManagerReady && frame.AddonIdentity == capturedAddon;
    public void RejectEffect(GuidedEffect effect, TimeSpan now)
    {
        if (effect.Generation != Generation || !OwnsWindowEdges) return;
        Pause("The requested action could not complete. Open the requested window manually, then select Continue.", now);
    }
    private int Attempts => source == GuidedSource.Dresser ? dresserAttempts : armoireAttempts;
    private GuidedState NeedState => source == GuidedSource.Dresser ? GuidedState.NeedDresser : GuidedState.NeedArmoire;
    private GuidedState InteractionState => source == GuidedSource.Dresser ? GuidedState.RequestDresserInteraction : GuidedState.RequestArmoireInteraction;
    private GuidedState WaitReadyState => source == GuidedSource.Dresser ? GuidedState.WaitForDresserReady : GuidedState.WaitForArmoireReady;
    private GuidedState CaptureState => source == GuidedSource.Dresser ? GuidedState.CaptureDresser : GuidedState.CaptureArmoire;
    private GuidedState CloseState => source == GuidedSource.Dresser ? GuidedState.RequestDresserClose : GuidedState.RequestArmoireClose;
    private GuidedState WaitClosedState => source == GuidedSource.Dresser ? GuidedState.WaitForDresserClosed : GuidedState.WaitForArmoireClosed;
    private GuidedEffect Effect(GuidedEffectKind kind) => new(kind, source, targetIdentity, capturedAddon, Generation);
    private void Transition(GuidedState state, TimeSpan now) { State = state; enteredAt = now; if (state is not (GuidedState.Paused or GuidedState.PausedWithPartial)) prompt = Label(state); }
    private void AdvanceAfterClose(TimeSpan now)
    {
        if (source == GuidedSource.Dresser)
        {
            source = GuidedSource.Armoire;
            retryAt = now;
            Transition(GuidedState.NeedArmoire, now);
        }
        else Transition(GuidedState.SaveFullSnapshot, now);
    }
    private void Pause(string message, TimeSpan now)
    {
        prompt = dresserCompleted && !armoireCompleted ? "Glamour Dresser captured; Armoire is incomplete. " + message : message;
        Transition(dresserCompleted != armoireCompleted ? GuidedState.PausedWithPartial : GuidedState.Paused, now);
    }
    public static bool IsActive(GuidedState state) => state is not (GuidedState.Idle or GuidedState.Completed or GuidedState.Cancelled or GuidedState.Failed);
    public static string Label(GuidedState state)
    {
        if (state == GuidedState.NeedDresser || state == GuidedState.RequestDresserInteraction || state == GuidedState.WaitForDresserReady) return "Opening Dresser";
        if (state == GuidedState.NeedArmoire || state == GuidedState.RequestArmoireInteraction || state == GuidedState.WaitForArmoireReady) return "Opening Armoire";
        return state switch
        {
            GuidedState.CaptureDresser => "Capturing Dresser",
            GuidedState.RequestDresserClose or GuidedState.WaitForDresserClosed => "Closing Dresser",
            GuidedState.CaptureArmoire => "Capturing Armoire",
            GuidedState.RequestArmoireClose or GuidedState.WaitForArmoireClosed => "Closing Armoire",
            GuidedState.SaveFullSnapshot or GuidedState.SavePartialSnapshot => "Saving Snapshot",
            GuidedState.Paused or GuidedState.PausedWithPartial => "Guided Capture Paused",
            GuidedState.Completed => "Current",
            GuidedState.Cancelled => "Cancelled",
            GuidedState.Failed => "Capture Failed",
            _ => "Ready",
        };
    }
}
