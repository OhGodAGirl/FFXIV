using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Saved-outfit selection survives transient native preview initialization without stale or unbounded retries.</summary>
public sealed class OutfitPreviewRequestTests
{
    private static readonly OutfitPreviewBinding Binding = new(123, Guid.Parse("11111111-1111-1111-1111-111111111111"), 7, PlateDyeMode.Saved);

    /// <summary>Selection immediately previews the complete immutable outfit in the currently selected dye mode.</summary>
    [Theory]
    [InlineData(0, 7, 9)]
    [InlineData(1, 0, 0)]
    public void SelectionUsesCurrentModeWithoutAToggle(int mode, byte firstDye, byte secondDye)
    {
        using var request = new OutfitPreviewRequest();
        var binding = Binding with { DyeMode = (PlateDyeMode)mode };
        request.Start(binding, Slots(), 1000);
        var calls = 0;
        var result = request.Advance(binding, 1000, (slots, selectedMode) =>
        {
            calls++;
            Assert.Equal(12, slots.Count);
            Assert.Equal(20u, slots[(int)PlateSlot.Body].ItemId);
            Assert.Equal(firstDye, OutfitPreviewPolicy.PreviewDye(slots[3].Stain0Id, selectedMode));
            Assert.Equal(secondDye, OutfitPreviewPolicy.PreviewDye(slots[3].Stain1Id, selectedMode));
            return OutfitPreviewAttempt.Applied;
        });
        Assert.Equal(OutfitPreviewProgress.Applied, result);
        Assert.False(request.IsPending);
        Assert.Equal(1, calls);
    }

    /// <summary>Readiness failure retains the selection, throttles attempts and applies once when initialization completes.</summary>
    [Fact]
    public void NotReadySelectionRetriesAndCompletesWithoutAnotherCommand()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        var calls = 0;
        OutfitPreviewAttempt Attempt(IReadOnlyList<SavedSlotView> _, PlateDyeMode mode)
        {
            calls++;
            Assert.Equal(PlateDyeMode.Saved, mode);
            return calls == 1 ? OutfitPreviewAttempt.NotReady : OutfitPreviewAttempt.Applied;
        }
        Assert.Equal(OutfitPreviewProgress.Waiting, request.Advance(Binding, 0, Attempt));
        Assert.Equal(OutfitPreviewProgress.Waiting, request.Advance(Binding, 99, Attempt));
        Assert.Equal(1, calls);
        Assert.Equal(OutfitPreviewProgress.Applied, request.Advance(Binding, 100, Attempt));
        Assert.Equal(OutfitPreviewProgress.Idle, request.Advance(Binding, 200, Attempt));
        Assert.Equal(2, calls);
    }

    /// <summary>A newer selection replaces prior retry state and snapshots the supplied appearance.</summary>
    [Fact]
    public void LatestSelectionWinsAndCannotBeMutatedByItsCaller()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        var latest = Binding with { PlateId = Guid.NewGuid(), DyeMode = PlateDyeMode.None };
        var input = Slots();
        input[3] = input[3] with { ItemId = 30 };
        request.Start(latest, input, 50);
        input[3] = input[3] with { ItemId = 99 };
        Assert.Equal(OutfitPreviewProgress.Applied, request.Advance(latest, 50, (slots, mode) =>
        {
            Assert.Equal(30u, slots[3].ItemId);
            Assert.Equal(PlateDyeMode.None, mode);
            return OutfitPreviewAttempt.Applied;
        }));
    }

    /// <summary>Character, selection, revision and mode changes invalidate retry intent before native calls.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ChangedContextCancelsBeforeNativeAccess(int changed)
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        var current = changed switch
        {
            0 => Binding with { CharacterId = 456 },
            1 => Binding with { PlateId = Guid.NewGuid() },
            2 => Binding with { Revision = 8 },
            _ => Binding with { DyeMode = PlateDyeMode.None },
        };
        var calls = 0;
        var result = request.Advance(current, 0, (_, _) => { calls++; return OutfitPreviewAttempt.Applied; });
        Assert.Equal(OutfitPreviewProgress.Cancelled, result);
        Assert.False(request.IsPending);
        Assert.Equal(0, calls);
    }

    /// <summary>The readiness budget cannot turn into an endless recurring framework operation.</summary>
    [Fact]
    public void ReadinessRetriesHaveAttemptAndTimeLimits()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        var calls = 0;
        for (var i = 0; i < OutfitPreviewRequest.MaximumAttempts; i++)
        {
            var outcome = request.Advance(Binding, i * OutfitPreviewRequest.RetryIntervalMilliseconds,
                (_, _) => { calls++; return OutfitPreviewAttempt.NotReady; });
            Assert.Equal(i == OutfitPreviewRequest.MaximumAttempts - 1 ? OutfitPreviewProgress.Expired : OutfitPreviewProgress.Waiting, outcome);
        }
        Assert.Equal(OutfitPreviewRequest.MaximumAttempts, calls);
        Assert.False(request.IsPending);
        request.Start(Binding, Slots(), 5000);
        Assert.Equal(OutfitPreviewProgress.Expired, request.Advance(Binding, 7000,
            (_, _) => throw new InvalidOperationException("Expired work must not touch native state.")));
    }

    /// <summary>Closing, opening native dye controls or loading clears retained intent; disposal permanently blocks it.</summary>
    [Fact]
    public void CancellationAndDisposalPreventFurtherCalls()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        request.Cancel();
        Assert.Equal(OutfitPreviewProgress.Idle, request.Advance(Binding, 100,
            (_, _) => throw new InvalidOperationException("Cancelled work must not run.")));
        request.Start(Binding, Slots(), 100);
        request.Dispose();
        request.Start(Binding, Slots(), 200);
        Assert.False(request.IsPending);
        Assert.Equal(OutfitPreviewProgress.Idle, request.Advance(Binding, 200,
            (_, _) => throw new InvalidOperationException("Disposed work must not run.")));
    }

    /// <summary>Native rejection or an ambiguous exception is terminal and never retried.</summary>
    [Fact]
    public void DoesNotRetryRejectedOrExceptionalNativeWrites()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        Assert.Equal(OutfitPreviewProgress.Rejected, request.Advance(Binding, 0, (_, _) => OutfitPreviewAttempt.Rejected));
        Assert.False(request.IsPending);
        request.Start(Binding, Slots(), 0);
        Assert.Throws<InvalidOperationException>(() => request.Advance(Binding, 0,
            (_, _) => throw new InvalidOperationException("Ambiguous native result")));
        Assert.False(request.IsPending);
    }

    /// <summary>Reentrant completion cannot discard a superseding selection.</summary>
    [Fact]
    public void OldCallbackCannotCompleteANewerRequest()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        var newer = Binding with { Revision = 8 };
        Assert.Equal(OutfitPreviewProgress.Cancelled, request.Advance(Binding, 0, (_, _) =>
        {
            request.Start(newer, Slots(), 0);
            return OutfitPreviewAttempt.Applied;
        }));
        Assert.True(request.IsPending);
        Assert.Equal(OutfitPreviewProgress.Applied, request.Advance(newer, 0, (_, _) => OutfitPreviewAttempt.Applied));
    }

    /// <summary>Invalid session facts or a backwards clock cannot prolong a request.</summary>
    [Fact]
    public void InvalidOwnerModeAndClockFailClosed()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding with { CharacterId = 0 }, Slots(), 0);
        Assert.False(request.IsPending);
        request.Start(Binding with { DyeMode = (PlateDyeMode)99 }, Slots(), 0);
        Assert.False(request.IsPending);
        request.Start(Binding, Slots(), 100);
        Assert.Equal(OutfitPreviewProgress.Expired, request.Advance(Binding, 99,
            (_, _) => throw new InvalidOperationException("A reversed clock must not dispatch.")));
    }

    private static SavedSlotView[] Slots()
        => Enum.GetValues<PlateSlot>().Select(slot => new SavedSlotView(slot, slot.ToString(), "Fixture",
            slot == PlateSlot.Body, 0, SharedOwnershipState.Unknown, "Unknown")
        {
            ItemId = slot == PlateSlot.Body ? 20u : 0,
            Stain0Id = 7,
            Stain1Id = 9,
        }).ToArray();
    /// <summary>The twentieth unsuccessful attempt expires before the independent lifetime limit.</summary>
    [Fact]
    public void AttemptLimitExpiresBeforeLifetime()
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        for (var attempt = 0; attempt < 19; attempt++)
            Assert.Equal(OutfitPreviewProgress.Waiting, request.Advance(Binding, attempt * 100, (_, _) => OutfitPreviewAttempt.NotReady));
        Assert.Equal(OutfitPreviewProgress.Expired, request.Advance(Binding, 1900, (_, _) => OutfitPreviewAttempt.NotReady));
        Assert.False(request.IsPending);
    }
    /// <summary>An expired request is removed so later frames cannot invoke its adapter.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2000)]
    public void ExpirationClearsPendingRequest(long time)
    {
        using var request = new OutfitPreviewRequest();
        request.Start(Binding, Slots(), 0);
        Assert.Equal(OutfitPreviewProgress.Expired, request.Advance(Binding, time, (_, _) => throw new InvalidOperationException()));
        Assert.False(request.IsPending);
        Assert.Equal(OutfitPreviewProgress.Idle, request.Advance(Binding, 100, (_, _) => throw new InvalidOperationException()));
    }
}
