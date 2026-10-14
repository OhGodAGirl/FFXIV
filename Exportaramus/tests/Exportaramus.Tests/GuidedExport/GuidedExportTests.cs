using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Tests.GuidedExport;

public sealed class GuidedExportTests
{
    private static readonly GuidedFurnishing Dresser = new(10, GuidedSource.Dresser, true, true, true, true, 2, 3);
    private static readonly GuidedFurnishing Armoire = Dresser with { Identity = 20, Source = GuidedSource.Armoire };
    private static GuidedFrame Frame(double seconds = 0) => new(TimeSpan.FromSeconds(seconds), 7, true, false, true, true, 10, [Dresser, Armoire], 42, false, false, false);
    private static GuidedFrame Ready(GuidedFrame frame) => frame with { ExpectedAddonPresent = true, AddonReady = true, ManagerReady = true };

    [Fact]
    public void FarArmoirePromptNamesTheRemainingSource()
    {
        var controller = new GuidedExportController();
        var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        CaptureAndClose(controller, frame, GuidedSource.Dresser);
        controller.Update(frame with { CurrentTarget = 20, Furnishings = [Armoire with { Distance = 10 }] });
        Assert.Contains("targeted Armoire", controller.View.Prompt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PausedCaptureAutomaticallyResumesOnlyAfterEveryReadinessCondition(int missing)
    {
        var controller = new GuidedExportController();
        var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        controller.Update(frame);
        var effect = controller.Update(frame);
        controller.RejectEffect(effect, frame.Now);
        var ready = Ready(frame);
        var incomplete = missing switch
        {
            0 => ready with { SafeConditions = false },
            1 => ready with { RuntimeValidated = false },
            2 => ready with { AddonReady = false },
            _ => ready with { ManagerReady = false },
        };
        controller.Update(incomplete);
        Assert.True(controller.View.Paused);
        controller.Update(ready);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Fact]
    public void RetryDelayDoesNotInteractAndFarTargetNeedsNoSelection()
    {
        var controller = new GuidedExportController();
        var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame with { Furnishings = [Dresser with { Distance = 10 }] }).Kind);
        controller.Update(frame);
        controller.Update(frame);
        controller.Update(Frame(1));
        Assert.Equal(GuidedState.NeedDresser, controller.State);
        Assert.Equal(GuidedEffectKind.None, controller.Update(Frame(1.05)).Kind);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Fact]
    public void MovingIntoRangeAutomaticallyTargetsAndInteractsWithoutContinue()
    {
        var controller = new GuidedExportController();
        var far = Frame() with { CurrentTarget = 99, Furnishings = [Dresser with { Distance = 10 }] };
        controller.Update(far, GuidedCommand.Start);
        Assert.Equal(GuidedEffectKind.SelectTarget, controller.Update(far).Kind);
        Assert.False(controller.View.Paused);
        Assert.Contains("Move closer", controller.View.Prompt);
        var near = Frame();
        controller.Update(near);
        Assert.Equal(GuidedEffectKind.Interact, controller.Update(near).Kind);
    }

    [Fact]
    public void AlreadyOpenArmoireIsCapturedWithoutFurnishingDiscoveryOrContinue()
    {
        var controller = new GuidedExportController(); var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        CaptureAndClose(controller, frame, GuidedSource.Dresser);
        controller.Update(frame with { Furnishings = [] });
        Assert.False(controller.View.Paused);
        var open = Ready(frame) with { Furnishings = [], AddonIdentity = 78 };
        controller.Update(open); controller.Update(open);
        var effect = controller.Update(open);
        Assert.Equal(GuidedEffectKind.Capture, effect.Kind);
        Assert.Equal(GuidedSource.Armoire, effect.Source);
        Assert.Equal(78UL, effect.AddonIdentity);
    }

    [Fact]
    public void ArmoireMenuIsSelectedOnceThenWaitsForTheActualCabinetBeforeCapture()
    {
        var controller = new GuidedExportController(); var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        CaptureAndClose(controller, frame, GuidedSource.Dresser);
        frame = frame with { CurrentTarget = 20, AddonIdentity = 43 };
        controller.Update(frame);
        Assert.Equal(GuidedEffectKind.Interact, controller.Update(frame).Kind);
        var menu = frame with { ArmoireMenuIdentity = 90 };
        var selection = controller.Update(menu);
        Assert.Equal(GuidedEffectKind.SelectArmoireMenu, selection.Kind);
        Assert.Equal(90UL, selection.AddonIdentity);
        Assert.Equal(20UL, selection.TargetIdentity);
        Assert.Equal(GuidedEffectKind.None, controller.Update(menu).Kind);
        Assert.Equal(GuidedState.WaitForArmoireReady, controller.State);
        controller.Update(Ready(frame));
        Assert.Equal(GuidedEffectKind.Capture, controller.Update(Ready(frame)).Kind);
    }

    [Fact]
    public void RejectedNativeEffectPausesAndStaleFailureCannotAffectANewRun()
    {
        var controller = new GuidedExportController(); var frame = Frame();
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame);
        var effect = controller.Update(frame);
        controller.RejectEffect(effect, frame.Now);
        Assert.True(controller.View.Paused);
        Assert.Contains("manually", controller.View.Prompt);
        controller.Update(frame, GuidedCommand.Cancel);
        controller.Update(frame, GuidedCommand.Start);
        controller.RejectEffect(effect, frame.Now);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Fact]
    public void CompleteRunCapturesAndClosesEachExactAddonThenRequestsOneFullSave()
    {
        var controller = new GuidedExportController(); var frame = Frame();
        Assert.Equal("", controller.View.Prompt);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame, GuidedCommand.Start).Kind);
        Assert.Equal(1, controller.Generation);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
        Assert.Equal(GuidedSource.Dresser, controller.CurrentSource);
        CaptureAndClose(controller, frame, GuidedSource.Dresser);
        Assert.Equal(GuidedState.NeedArmoire, controller.State);
        Assert.Equal(GuidedSource.Armoire, controller.CurrentSource);
        Assert.True(controller.View.DresserCompleted);
        CaptureAndClose(controller, frame with { CurrentTarget = 20, AddonIdentity = 43 }, GuidedSource.Armoire);
        Assert.Equal(GuidedEffectKind.SaveFull, controller.Update(frame).Kind);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        controller.Update(frame with { SaveResult = GuidedResult.Succeeded });
        Assert.Equal(GuidedState.Completed, controller.State);
        Assert.False(controller.OwnsWindowEdges);
        Assert.True(controller.View.ArmoireCompleted);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame, GuidedCommand.Cancel).Kind);
    }
    private static void CaptureAndClose(GuidedExportController controller, GuidedFrame frame, GuidedSource source)
    {
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.RequestDresserInteraction : GuidedState.RequestArmoireInteraction, controller.State);
        Assert.Equal(source == GuidedSource.Dresser ? "Opening Dresser" : "Opening Armoire", controller.View.StepLabel);
        var interaction = controller.Update(frame);
        Assert.Equal(GuidedEffectKind.Interact, interaction.Kind); Assert.Equal(source, interaction.Source);
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.WaitForDresserReady : GuidedState.WaitForArmoireReady, controller.State);
        var ready = Ready(frame); controller.Update(ready);
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.CaptureDresser : GuidedState.CaptureArmoire, controller.State);
        Assert.Equal(source == GuidedSource.Dresser ? "Capturing Dresser" : "Capturing Armoire", controller.View.StepLabel);
        Assert.Equal(GuidedEffectKind.Capture, controller.Update(ready).Kind);
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        controller.Update(ready with { CaptureResult = GuidedResult.Succeeded });
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.RequestDresserClose : GuidedState.RequestArmoireClose, controller.State);
        var close = controller.Update(ready);
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.WaitForDresserClosed : GuidedState.WaitForArmoireClosed, controller.State);
        Assert.Equal(source == GuidedSource.Dresser ? "Closing Dresser" : "Closing Armoire", controller.View.StepLabel);
        Assert.Equal(GuidedEffectKind.CloseAddon, close.Kind); Assert.Equal(frame.AddonIdentity, close.AddonIdentity);
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        controller.Update(frame);
    }

    [Fact]
    public void InteractionRevalidationRejectsTheChosenObjectEvenWhenAnotherEligibleObjectExists()
    {
        var controller = new GuidedExportController(); var frame = Frame();
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame);

        var effect = controller.Update(frame with { Furnishings = [Dresser with { Allowlisted = false }, Dresser with { Identity = 11 }] });

        Assert.Equal(GuidedEffectKind.None, effect.Kind); Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WaitingForReadinessRequiresBothTheAddonAndManager(bool addonReady, bool managerReady)
    {
        var controller = new GuidedExportController(); var frame = Frame();
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame); controller.Update(frame);

        controller.Update(frame with { ExpectedAddonPresent = true, AddonReady = addonReady, ManagerReady = managerReady });

        Assert.Equal(GuidedState.WaitForDresserReady, controller.State);
    }

    [Fact]
    public void DisappearingExpectedAddonCannotAuthorizeCaptureDespiteOtherReadyFlags()
    {
        var controller = new GuidedExportController(); var frame = Ready(Frame());
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame); controller.Update(frame);

        var effect = controller.Update(frame with { ExpectedAddonPresent = false });

        Assert.Equal(GuidedEffectKind.None, effect.Kind); Assert.True(controller.View.Paused);
    }

    [Fact]
    public void ManualDresserClosureDoesNotMakeALaterArmoirePauseCountAsACompletedCapture()
    {
        var controller = new GuidedExportController(); var ready = Ready(Frame());
        controller.Update(ready, GuidedCommand.Start); controller.Update(ready); controller.Update(ready); controller.Update(ready);
        controller.Update(ready with { CaptureResult = GuidedResult.Succeeded });
        controller.Update(ready with { ManagerReady = false });
        controller.Update(Frame(), GuidedCommand.Continue);
        Assert.Equal(GuidedState.NeedArmoire, controller.State);
        controller.Update(Frame() with { RuntimeValidated = false }); Assert.False(controller.View.Paused);

        controller.Update(Frame(), GuidedCommand.Continue);

        Assert.Equal(GuidedState.RequestArmoireInteraction, controller.State); Assert.False(controller.View.ArmoireCompleted);
    }

    [Fact]
    public void TimeoutIntervalsAreMeasuredFromNonzeroEntryTimes()
    {
        var controller = new GuidedExportController(); var frame = Frame(100);
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame); controller.Update(frame);
        controller.Update(frame with { Now = TimeSpan.FromMilliseconds(100899) });
        Assert.Equal(GuidedState.WaitForDresserReady, controller.State);
        controller.Update(frame with { Now = TimeSpan.FromMilliseconds(100900) });
        Assert.Equal(GuidedState.NeedDresser, controller.State);

        controller = new GuidedExportController(); frame = Ready(Frame(100));
        controller.Update(frame, GuidedCommand.Start); controller.Update(frame); controller.Update(frame); controller.Update(frame);
        controller.Update(frame with { CaptureResult = GuidedResult.Succeeded }); controller.Update(frame);
        controller.Update(frame with { Now = TimeSpan.FromSeconds(104) });
        Assert.Equal(GuidedState.WaitForDresserClosed, controller.State);
        controller.Update(frame with { Now = TimeSpan.FromSeconds(105) });
        Assert.True(controller.View.Paused);
    }

    [Theory]
    [InlineData(false, 7UL, false)]
    [InlineData(true, 0UL, false)]
    [InlineData(true, 7UL, true)]
    public void StartRequiresAnActiveCharacter(bool loggedIn, ulong identity, bool stopping)
    {
        var controller = new GuidedExportController(); controller.Update(Frame() with { LoggedIn = loggedIn, ContentId = identity, Stopping = stopping }, GuidedCommand.Start);
        Assert.Equal(GuidedState.Idle, controller.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EightAttemptsAreBoundedAndManualOpeningResumesWithoutContinue(bool armoire)
    {
        var controller = new GuidedExportController(); var frame = Frame(); controller.Update(frame, GuidedCommand.Start);
        if (armoire) { CaptureAndClose(controller, frame, GuidedSource.Dresser); frame = frame with { CurrentTarget = 20 }; }
        var interactions = 0;
        for (var tick = 0; tick < 150; tick++)
        {
            var effect = controller.Update(frame with { Now = TimeSpan.FromMilliseconds(tick * 150) });
            if (effect.Kind == GuidedEffectKind.Interact) interactions++;
        }
        Assert.Equal(8, interactions);
        Assert.False(controller.View.Paused);
        var ready = Ready(frame) with { Now = TimeSpan.FromSeconds(30) };
        controller.Update(ready); controller.Update(ready);
        Assert.Equal(GuidedEffectKind.Capture, controller.Update(ready).Kind);
    }

    [Fact]
    public void TargetIsSelectedOnceAndPlayerTargetChangePreventsInteraction()
    {
        var controller = new GuidedExportController(); var frame = Frame() with { CurrentTarget = 99 };
        controller.Update(frame, GuidedCommand.Start);
        var select = controller.Update(frame); Assert.Equal(GuidedEffectKind.SelectTarget, select.Kind); Assert.Equal(10UL, select.TargetIdentity);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind); Assert.Equal(GuidedState.NeedDresser, controller.State);
        controller.Update(frame with { CurrentTarget = 10 });
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame with { CurrentTarget = 10, Furnishings = [] }).Kind);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
        controller.Update(frame with { CurrentTarget = 10 }); controller.Update(Ready(frame) with { CurrentTarget = 10 });
        Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Fact]
    public void MissingFurnishingAndUnvalidatedOrUnsafeClientWaitWithoutInput()
    {
        foreach (var frame in new[] { Frame() with { Furnishings = [] }, Frame() with { RuntimeValidated = false }, Frame() with { SafeConditions = false } })
        {
            var controller = new GuidedExportController(); controller.Update(frame, GuidedCommand.Start);
            Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind); Assert.Equal(GuidedState.NeedDresser, controller.State);
            Assert.DoesNotContain("select Continue", controller.View.Prompt);
            Assert.DoesNotContain("Glamour Dresser captured", controller.View.Prompt);
            controller.Update(frame with { Now = TimeSpan.FromDays(1) }); Assert.Equal(GuidedState.NeedDresser, controller.State);
            controller.Update(frame, GuidedCommand.SavePartial); Assert.Equal(GuidedState.NeedDresser, controller.State);
            controller.Update(frame, GuidedCommand.Cancel); Assert.Equal(GuidedState.Cancelled, controller.State);
        }
    }

    [Fact]
    public void AppearedAddonGetsFifteenSecondsForReadinessAndIsNeverReadUntilComplete()
    {
        var controller = new GuidedExportController(); var frame = Frame(); controller.Update(frame, GuidedCommand.Start);
        controller.Update(frame); controller.Update(frame);
        frame = frame with { ExpectedAddonPresent = true, Now = TimeSpan.FromSeconds(9) };
        controller.Update(frame); controller.Update(frame with { Now = TimeSpan.FromMilliseconds(23999) });
        Assert.Equal(GuidedState.WaitForDresserReady, controller.State);
        controller.Update(frame with { Now = TimeSpan.FromSeconds(24) }); Assert.Equal(GuidedState.Paused, controller.State);
        Assert.Contains("Reopen", controller.View.Prompt);
        controller.Update(frame, GuidedCommand.Continue); controller.Update(frame);
        Assert.Equal(GuidedState.WaitForDresserReady, controller.State);
        controller.Update(frame with { ExpectedAddonPresent = false }); Assert.Equal(GuidedState.Paused, controller.State);
        Assert.Contains("Reopen", controller.View.Prompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneCloseRequestWaitsFiveSecondsThenRequiresManualClosure(bool armoire)
    {
        var controller = new GuidedExportController(); var frame = Frame(); controller.Update(frame, GuidedCommand.Start);
        if (armoire) { CaptureAndClose(controller, frame, GuidedSource.Dresser); frame = frame with { CurrentTarget = 20 }; }
        controller.Update(Ready(frame)); controller.Update(Ready(frame));
        controller.Update(Ready(frame)); controller.Update(Ready(frame) with { CaptureResult = GuidedResult.Succeeded });
        Assert.Equal(GuidedEffectKind.CloseAddon, controller.Update(Ready(frame)).Kind);
        controller.Update(Ready(frame) with { Now = TimeSpan.FromMilliseconds(4999) }); Assert.False(controller.View.Paused);
        controller.Update(Ready(frame) with { Now = TimeSpan.FromSeconds(5) }); Assert.True(controller.View.Paused);
        Assert.Contains("Close", controller.View.Prompt);
        Assert.Equal(!armoire, controller.View.Prompt.Contains("Glamour Dresser captured", StringComparison.Ordinal));
        Assert.Equal(!armoire, controller.View.CanSavePartial);
        controller.Update(Ready(frame) with { Now = TimeSpan.FromDays(1) }, GuidedCommand.Continue); Assert.True(controller.View.Paused);
        controller.Update(frame, GuidedCommand.Continue);
        Assert.Equal(armoire ? GuidedState.SaveFullSnapshot : GuidedState.NeedArmoire, controller.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialSaveOrCancelDoesNotRecaptureCompletedDresser(bool save)
    {
        var controller = new GuidedExportController(); var frame = Frame(); controller.Update(frame, GuidedCommand.Start);
        CaptureAndClose(controller, frame, GuidedSource.Dresser); controller.Update(frame with { Furnishings = [] });
        Assert.True(controller.View.CanSavePartial); Assert.False(controller.View.Paused);
        controller.Update(frame, save ? GuidedCommand.SavePartial : GuidedCommand.Cancel);
        var result = controller.Update(frame); Assert.Equal(save ? GuidedEffectKind.SavePartial : GuidedEffectKind.None, result.Kind);
        if (save) { controller.Update(frame with { SaveResult = GuidedResult.Failed }); Assert.Equal(GuidedState.Failed, controller.State); }
        else Assert.Equal(GuidedState.Cancelled, controller.State);
    }

    [Fact]
    public void CaptureAndCloseRevalidateExactAddonAndCaptureFailurePauses()
    {
        foreach (var failure in new[] { 0, 1, 2 })
        {
            var controller = new GuidedExportController(); var frame = Ready(Frame()); controller.Update(frame, GuidedCommand.Start);
            controller.Update(frame); controller.Update(frame);
            if (failure == 0) controller.Update(frame with { AddonIdentity = 99 });
            else
            {
                controller.Update(frame);
                controller.Update(frame with { CaptureResult = failure == 1 ? GuidedResult.Failed : GuidedResult.Succeeded });
                if (failure == 2) controller.Update(frame with { ManagerReady = false });
            }
            Assert.True(controller.View.Paused);
            Assert.Contains(failure == 2 ? "Close" : "Reopen", controller.View.Prompt);
            if (failure == 2)
            {
                controller.Update(frame with { ManagerReady = false }, GuidedCommand.Continue);
                Assert.True(controller.View.Paused);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IdentityLossLogoutAndDisposalInvalidateGeneration(int cause)
    {
        var controller = new GuidedExportController(); var frame = Frame(); controller.Update(frame, GuidedCommand.Start); var generation = controller.Generation;
        controller.Update(cause switch { 0 => frame with { ContentId = 8 }, 1 => frame with { LoggedIn = false }, _ => frame with { Stopping = true } });
        Assert.Equal(GuidedState.Cancelled, controller.State); Assert.True(controller.Generation > generation);
        controller.Update(frame, GuidedCommand.Start); Assert.Equal(GuidedState.NeedDresser, controller.State);
    }

    [Fact]
    public void LabelsNeverExposeImplementationStateNames()
    {
        foreach (var state in Enum.GetValues<GuidedState>())
        {
            var label = GuidedExportController.Label(state);
            Assert.NotEmpty(label); Assert.DoesNotContain("Request", label); Assert.DoesNotContain("WaitFor", label);
        }
    }

    [Fact]
    public void FurnishingSelectionPrefersCurrentTargetThenDistanceThenStableIdentity()
    {
        var farther = Dresser with { Identity = 11, Distance = 3 };
        Assert.Equal(farther, FurnishingPolicy.Select([farther, Dresser], GuidedSource.Dresser, 11));
        Assert.Equal(Dresser, FurnishingPolicy.Select([farther, Dresser], GuidedSource.Dresser, 0));
        Assert.Equal(Dresser, FurnishingPolicy.Select([Dresser with { Identity = 9, Source = GuidedSource.Armoire }, Dresser with { Identity = 12 }, Dresser], GuidedSource.Dresser, 0));
        Assert.Null(FurnishingPolicy.Select([], GuidedSource.Dresser, 0));
    }

    [Fact]
    public void EveryFurnishingSafetyFieldMustPassIndependently()
    {
        GuidedFurnishing[] invalid = [Dresser with { Identity = 0 }, Dresser with { Source = GuidedSource.Armoire }, Dresser with { Valid = false },
            Dresser with { Allowlisted = false }, Dresser with { ExpectedKind = false }, Dresser with { Targetable = false },
            Dresser with { Distance = double.NaN }, Dresser with { InteractionRange = double.PositiveInfinity }, Dresser with { Distance = -1 },
            Dresser with { InteractionRange = 0 }, Dresser with { Distance = 3.01 }];
        Assert.All(invalid, x => Assert.False(FurnishingPolicy.IsEligible(x, GuidedSource.Dresser)));
        Assert.True(FurnishingPolicy.IsEligible(Dresser with { Distance = 0 }, GuidedSource.Dresser));
        Assert.True(FurnishingPolicy.IsEligible(Dresser with { Distance = 3 }, GuidedSource.Dresser));
        Assert.False(FurnishingPolicy.IsEligible(Dresser with { Distance = 0, InteractionRange = 0 }, GuidedSource.Dresser));
    }
}
