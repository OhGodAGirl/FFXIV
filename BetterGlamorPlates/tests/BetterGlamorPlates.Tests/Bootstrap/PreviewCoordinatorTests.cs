using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies preview coordinator tests.</summary>
public sealed class PreviewCoordinatorTests
{
    /// <summary>A newer preview requested during native cleanup cannot be cancelled by the obsolete outer callback.</summary>
    [Fact]
    public void CleanupReentrancyPreservesTheNewerPreviewRequest()
    {
        using var fixture = new Fixture();
        fixture.Preview.Request(true);
        fixture.Context = fixture.Context with { WindowOpen = false };
        fixture.OnRelease = () =>
        {
            fixture.OnRelease = null;
            fixture.Context = fixture.Context with
            {
                WindowOpen = true,
                View = fixture.Context.View with { OutfitRevision = fixture.Context.View.OutfitRevision + 1 },
            };
            fixture.Preview.Request(true);
        };

        fixture.Preview.Advance();

        Assert.True(fixture.Preview.IsPending);
        Assert.Equal(2, fixture.Attempts.Count);
        Assert.Equal(1, fixture.Releases);
        Assert.Empty(fixture.Reports);
        fixture.Time = 100;
        fixture.Result = OutfitPreviewAttempt.Applied;
        fixture.Preview.Advance();
        Assert.False(fixture.Preview.IsPending);
        Assert.Equal(3, fixture.Attempts.Count);
        Assert.Equal([true], fixture.Reports);
    }
    /// <summary>An idle coordinator never reads native context or time after its host may have closed.</summary>
    [Fact]
    public void IdleAdvanceDoesNotTouchRuntimePorts()
    {
        using var preview = new PreviewCoordinator(() => throw new InvalidOperationException("context must not be read"),
            () => throw new InvalidOperationException("clock must not be read"),
            (_, _) => throw new InvalidOperationException("must not apply"),
            () => throw new InvalidOperationException("must not release"),
            _ => throw new InvalidOperationException("must not report"),
            () => throw new InvalidOperationException("must not reject"),
            _ => throw new InvalidOperationException("must not report error"));
        preview.Advance();
        Assert.False(preview.IsPending);
    }
    /// <summary>Verifies applied preview reports only when requested.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppliedPreviewReportsOnlyWhenRequested(bool report)
    {
        using var fixture = new Fixture();
        fixture.Result = OutfitPreviewAttempt.Applied;

        fixture.Preview.Request(report);
        fixture.Preview.Advance();

        Assert.Equal(report ? [true] : [], fixture.Reports);
        Assert.Single(fixture.Attempts);
        Assert.Equal(fixture.Context.View.Slots, fixture.Attempts[0].Slots);
        Assert.Equal(PlateDyeMode.None, fixture.Attempts[0].Mode);
        Assert.False(fixture.Preview.IsPending);
        Assert.Equal(0, fixture.Releases);
    }

    /// <summary>Verifies missing selection cancels and releases without attempt.</summary>
    [Fact]
    public void MissingSelectionCancelsAndReleasesWithoutAttempt()
    {
        using var fixture = new Fixture();
        fixture.Context = fixture.Context with { View = MainViewState.Empty };
        fixture.Preview.Request(true);
        Assert.False(fixture.Preview.IsPending);
        Assert.Equal(1, fixture.Releases);
        Assert.Empty(fixture.Attempts);
    }

    /// <summary>Verifies unavailable context cancels pending intent.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnavailableContextCancelsPendingIntent(int condition)
    {
        using var fixture = new Fixture();
        fixture.Preview.Request(true);
        Assert.True(fixture.Preview.IsPending);
        fixture.Context = condition switch
        {
            0 => fixture.Context with { WindowOpen = false },
            1 => fixture.Context with { EditorOpen = false },
            2 => fixture.Context with { DyeWindowOpen = true },
            _ => fixture.Context with { View = MainViewState.Empty },
        };

        fixture.Preview.Advance();
        fixture.Preview.Advance();

        Assert.False(fixture.Preview.IsPending);
        Assert.Single(fixture.Attempts);
        Assert.Equal(1, fixture.Releases);
        Assert.Empty(fixture.Reports);
    }

    /// <summary>Verifies rejection and expiry release and report failure.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectionAndExpiryReleaseAndReportFailure(bool expiry)
    {
        using var fixture = new Fixture();
        if (!expiry) fixture.Result = OutfitPreviewAttempt.Rejected;
        fixture.Preview.Request(true);
        if (expiry)
        {
            fixture.Time = OutfitPreviewRequest.LifetimeMilliseconds;
            fixture.Preview.Advance();
        }

        Assert.Equal([false], fixture.Reports);
        Assert.Equal(1, fixture.Releases);
        Assert.Equal(1, fixture.Rejections);
        Assert.Single(fixture.Attempts);
        Assert.False(fixture.Preview.IsPending);
    }

    /// <summary>Verifies native failure is reported and never retried.</summary>
    [Fact]
    public void NativeFailureIsReportedAndNeverRetried()
    {
        using var fixture = new Fixture { Failure = new InvalidOperationException("ambiguous native write") };
        fixture.Preview.Request(true);
        fixture.Time = 100;
        fixture.Preview.Advance();
        Assert.Same(fixture.Failure, Assert.Single(fixture.Errors));
        Assert.Single(fixture.Attempts);
        Assert.Equal([false], fixture.Reports);
        Assert.Equal(1, fixture.Releases);
        Assert.Equal(0, fixture.Rejections);
    }

    /// <summary>Verifies changed binding rejects old intent without reporting failure.</summary>
    [Fact]
    public void ChangedBindingRejectsOldIntentWithoutReportingFailure()
    {
        using var fixture = new Fixture();
        fixture.Preview.Request(true);
        fixture.Context = fixture.Context with { CharacterId = 999 };
        fixture.Time = 100;
        fixture.Preview.Advance();
        Assert.False(fixture.Preview.IsPending);
        Assert.Single(fixture.Attempts);
        Assert.Empty(fixture.Reports);
        Assert.Equal(0, fixture.Releases);
    }

    /// <summary>Verifies pending requests can be cancelled or disposed without further native attempts.</summary>
    [Fact]
    public void PendingRequestsCanBeCancelledOrDisposedWithoutFurtherNativeAttempts()
    {
        using var fixture = new Fixture();
        fixture.Preview.Request(true);
        fixture.Preview.Cancel();
        fixture.Preview.Advance();
        fixture.Preview.Request(false);
        fixture.Preview.Dispose();
        fixture.Preview.Advance();
        fixture.Preview.Request(true);
        Assert.Equal(2, fixture.Attempts.Count);
        Assert.False(fixture.Preview.IsPending);
        Assert.Equal(1, fixture.Releases);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly PreviewCoordinator Preview;
        internal PreviewContext Context = new(true, true, false, 42,
            MainViewState.Empty with
            {
                SelectedPlateId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OutfitRevision = 8,
                PreviewDyeMode = PlateDyeMode.None,
                Slots = [new(PlateSlot.Body, "Body", "Test", true, 1, SharedOwnershipState.Empty, null) { ItemId = 2 }],
            });
        internal long Time;
        internal OutfitPreviewAttempt Result = OutfitPreviewAttempt.NotReady;
        internal Exception? Failure;
        internal Action? OnRelease;
        internal readonly List<(IReadOnlyList<SavedSlotView> Slots, PlateDyeMode Mode)> Attempts = [];
        internal readonly List<bool> Reports = [];
        internal readonly List<Exception> Errors = [];
        internal int Releases;
        internal int Rejections;

        internal Fixture() => Preview = new(() => Context, () => Time, (slots, mode) =>
        {
            Attempts.Add((slots, mode));
            if (Failure is not null) throw Failure;
            return Result;
        }, () => { Releases++; OnRelease?.Invoke(); }, Reports.Add, () => Rejections++, Errors.Add);

        /// <summary>Verifies dispose.</summary>
        public void Dispose() => Preview.Dispose();
    }
}
