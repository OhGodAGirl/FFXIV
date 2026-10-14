using System.Collections.Immutable;
using System.Numerics;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Shared.Ui;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class MainUiFinalMutationTests
{
    [Fact]
    public void CaptureWithNoUsableCollectionsIsPartialRatherThanComplete()
    {
        var original = SnapshotFixture.Create();
        var state = CollectionStateFactory.Empty(original.State.ContentId, original.State.ProviderSessionId, SnapshotFixture.Now);
        var data = CollectionDataAssembler.Assemble(original.Catalog, state);
        Assert.Empty(data.Collections);
        var view = View(Bundle(data));
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.Equal("Some Data Unavailable", view.HealthHeadline);
    }

    [Fact]
    public void RestoredCollectionsNeverClaimACompleteCurrentCapture()
    {
        var original = SnapshotFixture.Create();
        var restored = CollectionStatePolicy.Restore(original.State, original.State.ContentId, Guid.NewGuid(), SnapshotFixture.Now);
        var view = View(Bundle(CollectionDataAssembler.Assemble(original.Catalog, restored)));
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.Equal("Restored snapshot", view.HealthFacts[0].Text);
    }

    [Theory]
    [InlineData(ReasonCode.CaptureFailed)]
    [InlineData(ReasonCode.MappingValidationFailed)]
    public void AReaderFailureKeepsOverallCapturePartial(ReasonCode reason)
    {
        var view = View(WithoutUnlock("mounts", reason));
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.Equal("Some Data Unavailable", view.HealthHeadline);
        Assert.Equal("Collector errors", view.HealthFacts[1].Text);
    }

    [Fact]
    public void AnUnopenedSupportedReaderDoesNotUndoOtherSuccessfulReads()
    {
        var view = View(WithoutUnlock("achievements", ReasonCode.NotLoadedThisSession));
        Assert.Equal(MainStatus.Current, view.Status);
        Assert.Equal("Waiting", view.Rows.Single(row => row.Key == "achievements").Count);
    }

    [Fact]
    public void AClosedPermanentCollectionIsNotAClosedPhysicalStorageObservation()
    {
        var original = SnapshotFixture.Create();
        var prior = original.State.Unlocks["survey_records"];
        var failed = new PermanentUnlockState([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 2,
            SnapshotFixture.Now, ReasonCode.WindowNotReady));
        var retained = CollectionStatePolicy.Resolve(prior, failed, true, true);
        var state = original.State with { Unlocks = original.State.Unlocks.SetItem("survey_records", retained) };
        var view = View(Bundle(CollectionDataAssembler.Assemble(original.Catalog, state)));
        var row = view.Rows.Single(value => value.Key == "survey_records");
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.Equal("!", row.Marker);
        Assert.Equal("Last-known count retained. " + failed.Health.Reason, row.Detail);
    }

    [Fact]
    public void FailedPhysicalReadExplainsFailureInsteadOfClaimingTheWindowClosed()
    {
        var view = View(RetainedDresser(ReasonCode.CaptureFailed));
        var row = view.Rows.Single(value => value.Key == "glamour_dresser");
        Assert.Equal(MainStatus.Partial, view.Status);
        Assert.Equal("!", row.Marker);
        Assert.Equal("Last-known count retained. The source could not be completely read. Try again when it is ready.", row.Detail);
    }

    [Fact]
    public void ClosedPhysicalWindowRetainsItsExactObservationExplanation()
    {
        var row = View(RetainedDresser(ReasonCode.WindowNotReady)).Rows.Single(value => value.Key == "glamour_dresser");
        Assert.Empty(row.Marker);
        Assert.Equal("Captured just now; window now closed. Last observed count retained.", row.Detail);
    }

    [Fact]
    public void FreshCollectionExplainsCompleteReadAndTheMeaningOfZero()
    {
        var row = View(Bundle(SnapshotFixture.Create())).Rows.Single(value => value.Key == "mounts");
        Assert.Equal("Captured just now. Complete read; zero means no owned entries.", row.Detail);
        Assert.Empty(row.Marker);
    }

    [Fact]
    public void ConfirmedPartialOutfitsHaveNoStaleObservationMarker()
    {
        var original = SnapshotFixture.Create();
        var definition = original.Catalog.Collections["outfits"];
        var second = definition.Items[0] with { Id = 45095, RequiredItemIds = [100] };
        var catalog = original.Catalog with { Collections = original.Catalog.Collections.SetItem("outfits", definition with { Items = [.. definition.Items, second] }) };
        var state = CollectionStateFactory.Empty(original.State.ContentId, original.State.ProviderSessionId, SnapshotFixture.Now);
        state = state with
        {
            CurrentItemPresence = state.CurrentItemPresence.SetItem("inventory",
                new([new(2642, 1), new(2965, 1), new(3747, 1)], true, SnapshotFixture.Health(DataType.CurrentItemPresence)))
        };
        var data = CollectionDataAssembler.Assemble(catalog, state);
        Assert.False(data.Collections.ContainsKey("outfits"));
        var bundle = Bundle(data) with { ConfirmedPartialCounts = ImmutableDictionary<string, int>.Empty.Add("outfits", 1) };
        var row = View(bundle).Rows.Single(value => value.Key == "outfits");
        Assert.Equal("1+", row.Count);
        Assert.Empty(row.Marker);
        Assert.Empty(row.Delta);
    }

    [Theory]
    [InlineData("achievements", ReasonCode.NotLoadedThisSession, "Open Achievements and wait for it to finish loading.")]
    [InlineData("frames", ReasonCode.NotLoadedThisSession, "Open the collection window and wait for it to finish loading.")]
    [InlineData("survey_records", ReasonCode.WindowNotReady, "Open the required window and keep it open until capture completes.")]
    [InlineData("mounts", ReasonCode.CaptureFailed, "Capture failed: CaptureFailed. The collection has not been treated as empty.")]
    [InlineData("mounts", ReasonCode.MappingValidationFailed, "Capture failed: MappingValidationFailed. The collection has not been treated as empty.")]
    [InlineData("mounts", ReasonCode.OutputSizeLimitExceeded, "Capture failed: OutputSizeLimitExceeded. The collection has not been treated as empty.")]
    public void MissingCollectionTooltipsKeepTheFullRecoveryInstruction(string key, ReasonCode reason, string expected)
    {
        Assert.Equal(expected, View(WithoutUnlock(key, reason)).Rows.Single(row => row.Key == key).Detail);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void DarkThemeInteractionSurfacesBrightenFromThePanel(uint selection)
    {
        var palette = ThemePolicy.Resolve(selection);
        Brighter(palette.Raised, palette.Panel);
        Brighter(palette.Alternate, palette.Panel);
        Brighter(palette.FrameHovered, palette.Panel);
        Brighter(palette.FrameActive, palette.FrameHovered);
        Brighter(palette.ButtonHovered, palette.Panel);
        Assert.Equal(palette.Icon, palette.ButtonActive);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void LightThemeInteractionSurfacesFollowSharedTransformations(uint selection)
    {
        var palette = ThemePolicy.Resolve(selection);
        Brighter(palette.Panel, palette.Raised);
        Brighter(palette.Panel, palette.Alternate);
        Brighter(palette.Panel, palette.FrameHovered);
        Brighter(palette.FrameHovered, palette.FrameActive);
        Brighter(palette.Panel, palette.ButtonHovered);
        Brighter(palette.ButtonHovered, palette.ButtonActive);
        Brighter(palette.Panel, palette.Header);
        Assert.Equal(palette.Icon, palette.HeaderHovered);
        Assert.Equal(palette.HeaderHovered, palette.HeaderActive);
    }

    private static void Brighter(Vector4 brighter, Vector4 darker)
    {
        Assert.True(brighter.X > darker.X && brighter.Y > darker.Y && brighter.Z > darker.Z);

    }

    private static AcceptedSnapshotBundle WithoutUnlock(string key, ReasonCode reason)
    {
        var original = SnapshotFixture.Create();
        var failed = new PermanentUnlockState([], CaptureReadPolicy.Health(DataType.PermanentUnlock, 2, SnapshotFixture.Now, reason));
        return Bundle(CollectionDataAssembler.Assemble(original.Catalog,
            original.State with { Unlocks = original.State.Unlocks.SetItem(key, failed) }));
    }

    private static AcceptedSnapshotBundle RetainedDresser(ReasonCode reason)
    {
        var original = SnapshotFixture.Create();
        var failed = CaptureReadPolicy.FailedPresence(false, 2, SnapshotFixture.Now, reason);
        var retained = CollectionStatePolicy.Resolve(original.State.CurrentItemPresence["glamourDresser"], failed, true, true);
        return Bundle(CollectionDataAssembler.Assemble(original.Catalog,
            original.State with { CurrentItemPresence = original.State.CurrentItemPresence.SetItem("glamourDresser", retained) }));
    }

    private static AcceptedSnapshotBundle Bundle(CollectionData data) => new(data, 12, "{}", null, "fingerprint",
        ImmutableDictionary<string, string>.Empty, PreparedFFXIVCollectExport.Unavailable("No prepared export."), 0, null)
    {
        CollectionOwnedCounts = data.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Count(item => item.Owned))
    };

    private static MainViewState View(AcceptedSnapshotBundle bundle) => MainSnapshotPresenter.Prepare(bundle, true, null,
        false, false, new GuidedExportController().View, SnapshotFixture.Now, true);
}
