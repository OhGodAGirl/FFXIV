using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Tests.GuidedExport;

public sealed class GuidedFinalMutationTests
{
    private static readonly GuidedFurnishing Dresser = new(10, GuidedSource.Dresser, true, true, true, true, 2, 3);
    private static readonly GuidedFurnishing Armoire = Dresser with { Identity = 20, Source = GuidedSource.Armoire };

    [Theory]
    [InlineData(GuidedSource.Dresser)]
    [InlineData(GuidedSource.Armoire)]
    public void DiscoveryAcceptsAValidFurnishingAtThePlayersPosition(GuidedSource source)
    {
        var furnishing = Dresser with { Source = source, Distance = 0 };

        Assert.True(FurnishingPolicy.IsDiscoverable(furnishing, source));
        Assert.Same(furnishing, FurnishingPolicy.Discover([furnishing], source, 0));
    }

    [Fact]
    public void DiscoveryChoosesTheNearestCandidateWithoutAMatchingTarget()
    {
        var near = Dresser with { Identity = 30, Distance = 4 };
        var far = Dresser with { Identity = 5, Distance = 12 };

        Assert.Same(near, FurnishingPolicy.Discover([far, near], GuidedSource.Dresser, 99));
        Assert.Same(near, FurnishingPolicy.Discover([near, far], GuidedSource.Dresser, 0));
    }

    [Fact]
    public void EquidistantDiscoveryUsesTheLowestIdentityRegardlessOfInputOrder()
    {
        var low = Dresser with { Identity = 5 };
        var high = Dresser with { Identity = 30 };

        Assert.Same(low, FurnishingPolicy.Discover([high, low], GuidedSource.Dresser, 0));
        Assert.Same(low, FurnishingPolicy.Discover([low, high], GuidedSource.Dresser, 0));
    }

    [Fact]
    public void AValidCurrentTargetTakesPriorityOverANearerFurnishing()
    {
        var near = Dresser with { Identity = 5, Distance = 1 };
        var targeted = Dresser with { Identity = 30, Distance = 12 };

        Assert.Same(targeted, FurnishingPolicy.Discover([near, targeted], GuidedSource.Dresser, 30));
        Assert.Same(targeted, FurnishingPolicy.Discover([targeted, near], GuidedSource.Dresser, 30));
        Assert.Same(near, FurnishingPolicy.Discover([near, targeted with { Targetable = false }], GuidedSource.Dresser, 30));
    }

    [Theory]
    [InlineData(false, true, "Waiting for Guided Capture compatibility. Cancel and check the game build if this persists.")]
    [InlineData(false, false, "Waiting for Guided Capture compatibility. Cancel and check the game build if this persists.")]
    [InlineData(true, false, "Waiting until it is safe to continue. Capture resumes automatically.")]
    public void WaitingPromptExplainsTheCurrentCompatibilityOrSafetyBlock(bool runtimeValidated, bool safe, string expected)
    {
        var controller = new GuidedExportController();
        var frame = Frame() with { RuntimeValidated = runtimeValidated, SafeConditions = safe };
        controller.Update(frame, GuidedCommand.Start);

        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        Assert.Equal(GuidedState.NeedDresser, controller.State);
        Assert.Equal(expected, controller.View.Prompt);
        Assert.False(controller.View.Paused);

        controller.Update(frame with { RuntimeValidated = true, SafeConditions = true });
        Assert.Equal(GuidedState.RequestDresserInteraction, controller.State);
        Assert.Equal("Opening Dresser", controller.View.Prompt);
    }

    [Theory]
    [InlineData(GuidedSource.Dresser, "Step 1 of 2: Move near a Glamour Dresser. It will open automatically when you are in range.")]
    [InlineData(GuidedSource.Armoire, "Step 2 of 2: Move near an Armoire. It will open automatically when you are in range.")]
    public void MissingFurnishingPromptNamesTheRemainingStep(GuidedSource source, string expected)
    {
        var (controller, frame) = BeginSource(source);

        Assert.Equal(GuidedEffectKind.None, controller.Update(frame with { Furnishings = [] }).Kind);
        Assert.Equal(NeedState(source), controller.State);
        Assert.Equal(expected, controller.View.Prompt);
        Assert.False(controller.View.Paused);
        Assert.Equal(source == GuidedSource.Armoire, controller.View.CanSavePartial);
    }

    [Theory]
    [InlineData(GuidedSource.Dresser, "Glamour Dresser")]
    [InlineData(GuidedSource.Armoire, "Armoire")]
    public void OutOfRangePromptNamesTheTargetAndLeavesMovementToThePlayer(GuidedSource source, string name)
    {
        var (controller, frame) = BeginSource(source);
        var far = (source == GuidedSource.Dresser ? Dresser : Armoire) with { Distance = 12.5 };
        frame = frame with { Furnishings = [far], CurrentTarget = 99 };

        var selection = controller.Update(frame);
        Assert.Equal(GuidedEffectKind.SelectTarget, selection.Kind);
        Assert.Equal(far.Identity, selection.TargetIdentity);
        Assert.Equal($"Move closer to the targeted {name} ({12.5:F1} yalms). Movement is left to you.", controller.View.Prompt);
        Assert.Equal(NeedState(source), controller.State);
        Assert.False(controller.View.Paused);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame with { CurrentTarget = far.Identity }).Kind);
    }

    [Theory]
    [InlineData(GuidedSource.Dresser, "Automatic interaction paused; open the Glamour Dresser manually. Capture will continue automatically.")]
    [InlineData(GuidedSource.Armoire, "Automatic interaction paused; open the Armoire manually. Capture will continue automatically.")]
    public void TheEighthAppearanceTimeoutImmediatelyOffersManualOpeningWithoutAnotherAttempt(GuidedSource source, string expected)
    {
        var (controller, frame) = BeginSource(source);
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
            var interaction = controller.Update(frame);
            Assert.Equal(GuidedEffectKind.Interact, interaction.Kind);
            Assert.Equal(source, interaction.Source);
            Assert.Equal(frame.CurrentTarget, interaction.TargetIdentity);

            frame = frame with { Now = frame.Now + TimeSpan.FromMilliseconds(899) };
            Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
            Assert.Equal(WaitReadyState(source), controller.State);
            frame = frame with { Now = frame.Now + TimeSpan.FromMilliseconds(1) };
            Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
            Assert.Equal(NeedState(source), controller.State);

            if (attempt == 8)
            {
                Assert.Equal("Automatic interaction paused. Open the window manually to continue automatically.", controller.View.Prompt);
                break;
            }

            Assert.Equal(source == GuidedSource.Dresser ? "Opening Dresser" : "Opening Armoire", controller.View.Prompt);
            Assert.Equal(GuidedEffectKind.None, controller.Update(frame with { Now = frame.Now + TimeSpan.FromMilliseconds(149) }).Kind);
            Assert.Equal(NeedState(source), controller.State);
            frame = frame with { Now = frame.Now + TimeSpan.FromMilliseconds(150) };
        }

        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        Assert.Equal(expected, controller.View.Prompt);
        Assert.False(controller.View.Paused);
        Assert.Equal(NeedState(source), controller.State);
        frame = frame with { Now = frame.Now + TimeSpan.FromSeconds(30) };
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        Assert.Equal(expected, controller.View.Prompt);

        var ready = Ready(frame) with { Furnishings = [] };
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        Assert.Equal(CaptureState(source), controller.State);
        Assert.Equal(GuidedEffectKind.Capture, controller.Update(ready).Kind);
    }

    [Theory]
    [InlineData(GuidedSource.Dresser)]
    [InlineData(GuidedSource.Armoire)]
    public void AReadyExpectedAddonTakesPrecedenceOverAPendingMenu(GuidedSource source)
    {
        var (controller, frame) = BeginSource(source);
        controller.Update(frame);
        Assert.Equal(GuidedEffectKind.Interact, controller.Update(frame).Kind);
        var ready = Ready(frame) with { ArmoireMenuIdentity = 90 };

        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        Assert.Equal(CaptureState(source), controller.State);
        var capture = controller.Update(ready);
        Assert.Equal(GuidedEffectKind.Capture, capture.Kind);
        Assert.Equal(source, capture.Source);
        Assert.Equal(frame.AddonIdentity, capture.AddonIdentity);
    }

    [Fact]
    public void RestartAfterAMenuRequestResetsMenuOwnershipEvenWhenThePlayerOpensBothWindows()
    {
        var (controller, frame) = BeginSource(GuidedSource.Armoire);
        controller.Update(frame);
        Assert.Equal(GuidedEffectKind.Interact, controller.Update(frame).Kind);
        Assert.Equal(GuidedEffectKind.SelectArmoireMenu, controller.Update(frame with { ArmoireMenuIdentity = 90 }).Kind);
        controller.Update(frame, GuidedCommand.Cancel);

        frame = Frame() with { Now = TimeSpan.FromSeconds(1) };
        controller.Update(frame, GuidedCommand.Start);
        CaptureOpenWindow(controller, frame, GuidedSource.Dresser);
        Assert.Equal(GuidedEffectKind.CloseAddon, controller.Update(Ready(frame)).Kind);
        controller.Update(frame);
        Assert.Equal(GuidedState.NeedArmoire, controller.State);

        frame = frame with { CurrentTarget = 20, AddonIdentity = 43 };
        Assert.Equal(GuidedEffectKind.None, controller.Update(Ready(frame)).Kind);
        Assert.Equal(GuidedState.WaitForArmoireReady, controller.State);
        var menu = controller.Update(frame with { ArmoireMenuIdentity = 91 });
        Assert.Equal(GuidedEffectKind.SelectArmoireMenu, menu.Kind);
        Assert.Equal(91UL, menu.AddonIdentity);
        Assert.Equal(GuidedSource.Armoire, menu.Source);
        Assert.NotEqual(GuidedEffectKind.SelectArmoireMenu, controller.Update(frame with { ArmoireMenuIdentity = 91 }).Kind);
    }

    [Fact]
    public void ContinuingAfterManualDresserClosureDoesNotCarryTheCloseWaitIntoAnArmoireFailure()
    {
        var (controller, frame) = BeginSource(GuidedSource.Dresser);
        CaptureOpenWindow(controller, frame, GuidedSource.Dresser);
        Assert.Equal(GuidedEffectKind.CloseAddon, controller.Update(Ready(frame)).Kind);
        frame = frame with { Now = TimeSpan.FromSeconds(5) };
        controller.Update(Ready(frame));
        Assert.Equal(GuidedState.PausedWithPartial, controller.State);
        Assert.Equal("Glamour Dresser captured; Armoire is incomplete. Close the captured window manually, then select Continue.", controller.View.Prompt);

        controller.Update(frame, GuidedCommand.Continue);
        Assert.Equal(GuidedState.NeedArmoire, controller.State);
        frame = frame with { CurrentTarget = 20, AddonIdentity = 43 };
        controller.Update(frame);
        var interaction = controller.Update(frame);
        Assert.Equal(GuidedEffectKind.Interact, interaction.Kind);
        controller.RejectEffect(interaction, frame.Now);
        Assert.Equal(GuidedState.PausedWithPartial, controller.State);

        Assert.Equal(GuidedEffectKind.None, controller.Update(frame, GuidedCommand.Continue).Kind);
        Assert.Equal(GuidedState.NeedArmoire, controller.State);
        Assert.True(controller.View.DresserCompleted);
        Assert.False(controller.View.ArmoireCompleted);
        Assert.Equal(GuidedEffectKind.None, controller.Update(frame).Kind);
        Assert.Equal(GuidedState.RequestArmoireInteraction, controller.State);
    }

    [Fact]
    public void FailedArmoireCaptureExposesThePartialPauseStateAndCanSaveOnlyTheCompletedDresser()
    {
        var (controller, frame) = BeginSource(GuidedSource.Armoire);
        var ready = Ready(frame);
        controller.Update(ready);
        controller.Update(ready);
        Assert.Equal(GuidedEffectKind.Capture, controller.Update(ready).Kind);

        controller.Update(ready with { CaptureResult = GuidedResult.Failed });
        Assert.Equal(GuidedState.PausedWithPartial, controller.State);
        Assert.Equal(GuidedState.PausedWithPartial, controller.View.State);
        Assert.Equal("Glamour Dresser captured; Armoire is incomplete. Capture failed. Reopen the requested window manually, then select Continue.", controller.View.Prompt);
        Assert.True(controller.View.Paused);
        Assert.True(controller.View.CanSavePartial);
        Assert.True(controller.View.DresserCompleted);
        Assert.False(controller.View.ArmoireCompleted);

        Assert.Equal(GuidedEffectKind.None, controller.Update(frame, GuidedCommand.SavePartial).Kind);
        Assert.Equal(GuidedState.SavePartialSnapshot, controller.State);
        Assert.Equal(GuidedEffectKind.SavePartial, controller.Update(frame).Kind);
    }

    private static GuidedFrame Frame() => new(TimeSpan.Zero, 7, true, false, true, true, 10, [Dresser, Armoire], 42, false, false, false);

    private static GuidedFrame Ready(GuidedFrame frame) => frame with { ExpectedAddonPresent = true, AddonReady = true, ManagerReady = true };

    private static GuidedState NeedState(GuidedSource source) => source == GuidedSource.Dresser ? GuidedState.NeedDresser : GuidedState.NeedArmoire;

    private static GuidedState WaitReadyState(GuidedSource source) => source == GuidedSource.Dresser ? GuidedState.WaitForDresserReady : GuidedState.WaitForArmoireReady;

    private static GuidedState CaptureState(GuidedSource source) => source == GuidedSource.Dresser ? GuidedState.CaptureDresser : GuidedState.CaptureArmoire;

    private static (GuidedExportController Controller, GuidedFrame Frame) BeginSource(GuidedSource source)
    {
        var controller = new GuidedExportController();
        var frame = Frame();
        controller.Update(frame, GuidedCommand.Start);
        if (source == GuidedSource.Armoire)
        {
            CaptureOpenWindow(controller, frame, GuidedSource.Dresser);
            Assert.Equal(GuidedEffectKind.CloseAddon, controller.Update(Ready(frame)).Kind);
            controller.Update(frame);
            frame = frame with { CurrentTarget = 20, AddonIdentity = 43 };
        }

        Assert.Equal(NeedState(source), controller.State);
        return (controller, frame);
    }

    private static void CaptureOpenWindow(GuidedExportController controller, GuidedFrame frame, GuidedSource source)
    {
        var ready = Ready(frame);
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        Assert.Equal(WaitReadyState(source), controller.State);
        Assert.Equal(GuidedEffectKind.None, controller.Update(ready).Kind);
        Assert.Equal(CaptureState(source), controller.State);
        var capture = controller.Update(ready);
        Assert.Equal(GuidedEffectKind.Capture, capture.Kind);
        Assert.Equal(source, capture.Source);
        controller.Update(ready with { CaptureResult = GuidedResult.Succeeded });
        Assert.Equal(source == GuidedSource.Dresser ? GuidedState.RequestDresserClose : GuidedState.RequestArmoireClose, controller.State);
    }
}
