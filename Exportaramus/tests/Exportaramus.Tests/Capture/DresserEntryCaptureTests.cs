using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class DresserEntryCaptureTests
{
    private static readonly ImmutableHashSet<uint> Known = [100, 101, 200];
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-12T00:00:00Z");

    [Fact]
    public void PhysicalCopiesAndOutfitSlotsRemainWhilePresenceDeduplicates()
    {
        var ordinary = DresserCapturePolicy.Entry(4, 1_000_100, Known);
        var duplicate = DresserCapturePolicy.Entry(7, 100, Known);
        var outfit = DresserCapturePolicy.Entry(9, 200, Known,
            [100, 101, 100, 0, 0, 0, 0, 0, 0, 0, 0], [true, false, true, false, false, false, false, false, false, false, false]);
        var state = DresserCapturePolicy.Complete([ordinary, duplicate, outfit], 42, Now);
        Assert.Equal(3, state.DresserEntries!.Value.Length);
        Assert.Equal(1_000_100u, ordinary.RawItemId);
        Assert.Null(ordinary.OutfitId);
        Assert.Equal(new DresserEntryComponent(0, 100), Assert.Single(ordinary.Components));
        Assert.Equal(200u, outfit.OutfitId);
        Assert.Equal(new[] { 0, 2 }, outfit.Components.Select(c => c.SlotIndex));
        Assert.Equal(100u, Assert.Single(state.Items).ItemId);
        Assert.Equal(42, state.Health.CaptureGeneration);
        Assert.True(state.Health.IsUsable);
    }

    [Fact]
    public void CompleteEmptyReplacesBothViewsAndFailureRetainsBothOrClearsBoth()
    {
        var prior = DresserCapturePolicy.Complete([DresserCapturePolicy.Entry(0, 100, Known)], 1, Now);
        var empty = DresserCapturePolicy.Complete([], 2, Now);
        var replaced = CollectionStatePolicy.Resolve(prior, empty, true, true);
        Assert.Empty(replaced.Items);
        Assert.Empty(replaced.DresserEntries!.Value);
        var failure = CaptureReadPolicy.FailedPresence(false, 3, Now, ReasonCode.WindowNotReady);
        var retained = CollectionStatePolicy.Resolve(prior, failure, true, true);
        Assert.Equal(prior.Items, retained.Items);
        Assert.Equal(prior.DresserEntries, retained.DresserEntries);
        Assert.Equal(prior.Health.CaptureGeneration, retained.Health.CaptureGeneration);
        Assert.Equal(SourceStatus.Stale, retained.Health.SourceStatus);
        var switched = CollectionStatePolicy.Resolve(prior, failure, false, true);
        Assert.Empty(switched.Items);
        Assert.Null(switched.DresserEntries);
    }

    [Fact]
    public void InvalidOrEmptyUnlockedMappingCannotProduceCompleteEmpty()
    {
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 200, Known,
            [999, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [true, false, false, false, false, false, false, false, false, false, false]));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 200, Known,
            [100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [false, false, false, false, false, false, false, false, false, false, false]));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(800, 100, Known));
        var entry = DresserCapturePolicy.Entry(0, 100, Known);
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Complete([entry, entry], 1, Now));
    }
    [Fact]
    public void EmptyMappedSlotsAreSkippedAndInvalidMappingCannotRetainPriorEntries()
    {
        var entry = DresserCapturePolicy.Entry(0, 200, Known,
            [100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [true, true, false, false, false, false, false, false, false, false, false]);
        Assert.Equal(new DresserEntryComponent(0, 100), Assert.Single(entry.Components));
        var prior = DresserCapturePolicy.Complete([entry], 1, Now);
        var error = Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(800, 100, Known));
        Assert.Equal(ReasonCode.MappingValidationFailed, error.Code);
        var failed = CaptureReadPolicy.FailedPresence(false, 2, Now, error.Code);
        var resolved = CollectionStatePolicy.Resolve(prior, failed, true, true);
        Assert.Equal(SourceStatus.Unavailable, resolved.Health.SourceStatus);
        Assert.Empty(resolved.Items);
        Assert.Null(resolved.DresserEntries);
    }
}



