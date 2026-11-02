using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Observable state and failure evidence remain accurate across draft and load transitions.</summary>
public sealed class MainControllerEvidenceTests
{
    /// <summary>Legacy order migration handles mixed old and new entries with stable tie breaks.</summary>
    [Fact]
    public void MixedLegacyOrderUsesNameThenIdentityTieBreak()
    {
        var first = PlateLibrary.Create("Alpha", TestDomainValues.Timestamp, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = PlateLibrary.Create("Alpha", TestDomainValues.Timestamp, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var third = PlateLibrary.Create("Zulu", TestDomainValues.Timestamp, Guid.NewGuid());
        first.LibraryOrder = 10;
        var store = new FakePlateLibraryStore(third, second, first);

        using var controller = Create(store);

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, store.State.SavedPlates.Select(plate => plate.Id));
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, controller.View.SavedPlates.Select(plate => plate.Id));
    }

    /// <summary>Arbitrary installed icon ids require an explicit validator; valid custom icons win over equipment icons.</summary>
    [Fact]
    public void IconValidationAndCustomIconPrecedenceAreExplicit()
    {
        var plate = TestDomainValues.Plate("Icon", 20);
        plate.IconId = 42;
        var facts = Facts();
        facts.Icons[20] = 10;
        using (var defaultValidator = Create(new(plate), facts: facts))
            Assert.Equal(10u, defaultValidator.View.ActivePlate!.IconId);
        plate.IconId = 42;
        using var allowed = new MainController(new FakePlateLibraryStore(plate), new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts, isGameIcon: id => id == 42);
        Assert.Equal(42u, allowed.View.ActivePlate!.IconId);
    }

    /// <summary>Reset restores saved items and advances the binding revision so queued loads cannot reuse it.</summary>
    [Fact]
    public void ResetRestoresItemsAndInvalidatesOldRevision()
    {
        var plate = TestDomainValues.Plate("Draft", 20);
        using var controller = Create(new(plate));
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body, plate.Id, 20), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;

        controller.Execute(new MainCommand.DiscardOutfit(plate.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(20u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.OutfitRevision > revision);
    }

    /// <summary>Re-selecting a plate preserves its draft while invalidating previous load requests.</summary>
    [Fact]
    public void SelectingSamePlatePreservesDraftAndChangesRevision()
    {
        var plate = TestDomainValues.Plate("Draft", 20);
        using var controller = Create(new(plate));
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body, plate.Id, 20), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;

        controller.Execute(new MainCommand.SelectPlate(plate.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
        Assert.True(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.OutfitRevision > revision);
    }

    /// <summary>Load readiness reports the actual active plate and source availability checks.</summary>
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void RejectedLoadCapturesReadiness(bool hasPlate, bool editorOpen, bool dresser, bool armoire)
    {
        var journal = new DiagnosticJournal();
        var store = hasPlate ? new FakePlateLibraryStore(TestDomainValues.Plate("Load", 20)) : new FakePlateLibraryStore();
        var editor = new FakePlateEditor { Open = editorOpen };
        var native = new FakeNativeCollectionValidator { Snapshot = new([], dresser, armoire, TestDomainValues.Timestamp) };
        using var controller = Create(store, editor, native, journal: journal);

        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(DiagnosticPhase.LoadValidation, journal.Current.Phase);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Equal(hasPlate ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.ActivePlate).Observed);
        if (hasPlate)
            Assert.Equal(editorOpen ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.EditorVisible).Observed);
        if (hasPlate && editorOpen)
        {
            Assert.Equal(dresser ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.DresserLoaded).Observed);
            Assert.Equal(armoire ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.ArmoireLoaded).Observed);
        }
        Assert.Equal(0, editor.ApplyCount);
    }

    /// <summary>Inventory source lookup must match both item and location kind before copying stored dyes.</summary>
    [Fact]
    public void AddItemCopiesOnlyMatchingSourceDyes()
    {
        var plate = TestDomainValues.Plate("Source");
        var native = new FakeNativeCollectionValidator
        {
            Snapshot = new([
            new(99, CollectionSource.Dresser, 0, 8, 8),
            new(20, CollectionSource.Armoire, 0, 7, 7),
            new(20, CollectionSource.Dresser, 1, 2, 3)], true, true, TestDomainValues.Timestamp)
        };
        using var controller = Create(new(plate), native: native);

        controller.Execute(new MainCommand.AddItem(PlateSlot.Body, TestDomainValues.Item(20, "Item")), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(2, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(3, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
    }

    /// <summary>Restoring stored dyes prefers the exact appearance even when another copy sorts first.</summary>
    [Fact]
    public void RestoreStoredDyesPrefersExactAppearance()
    {
        var plate = TestDomainValues.Plate("Dyes", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 2;
        var native = new FakeNativeCollectionValidator
        {
            Snapshot = new([
            new(20, CollectionSource.Dresser, 0, 8, 0),
            new(20, CollectionSource.Dresser, 1, 2, 0)], true, true, TestDomainValues.Timestamp)
        };
        using var controller = Create(new(plate), native: native);

        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(2, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal("Restored this item's stored dyes in the local outfit. No dye was consumed.", controller.View.Status);
    }

    /// <summary>Failures name the needed dye, fallback colour, or Terebinth precisely.</summary>
    [Theory]
    [InlineData(0, "Terebinth")]
    [InlineData(7, "dye for Ruby Red")]
    [InlineData(8, "dye for the saved colour")]
    public void MissingDyeMessageNamesRequiredMaterial(byte dye, string expected)
    {
        var editor = new FakePlateEditor
        {
            ApplyResult = new(0, 0,
            [new(PlateApplyFailureKind.DyeUnavailable, PlateSlot.Body, 20) { DyeId = dye }])
        };
        var native = new FakeNativeCollectionValidator { Snapshot = new([new(20, CollectionSource.Dresser, 0, 0, 0)], true, true, TestDomainValues.Timestamp) };
        using var controller = new MainController(new FakePlateLibraryStore(TestDomainValues.Plate("Load", 20)), native,
            new FakeSharedCollectionSnapshotSource(), editor, Facts(), dyes: [new(7, "Ruby Red", 0, false, "")]);

        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal($"Loaded 0 items. Body: not enough {expected} in your inventory. No slots were changed.", controller.View.OperationFeedback);
    }

    private static FakeItemFacts Facts()
    {
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        facts.DyeCounts[20] = 2;
        return facts;
    }

    /// <summary>Unknown or unsupported channel counts are distinct from a supported undyeable item.</summary>
    [Theory]
    [InlineData(false, 0, false, 0)]
    [InlineData(true, 0, true, 0)]
    [InlineData(true, 2, true, 2)]
    [InlineData(true, 3, false, 0)]
    public void ViewReportsDyeCapability(bool known, byte count, bool available, byte displayedCount)
    {
        var facts = known ? Facts() : new FakeItemFacts();
        facts.DyeCounts[20] = count;
        using var controller = Create(new(TestDomainValues.Plate("Channels", 20)), facts: facts);

        var slot = controller.View.Slots[(int)PlateSlot.Body];

        Assert.Equal(available, slot.DyeFactsAvailable);
        Assert.Equal(displayedCount, slot.DyeCount);
        Assert.False(controller.View.Slots[(int)PlateSlot.Head].DyeFactsAvailable);
    }

    /// <summary>A rejected dye selection records its target, channel and palette checks.</summary>
    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 2, 0)]
    [InlineData(true, 0, 7)]
    public void RejectedDyeSelectionRetainsEvidence(bool targetMatches, byte channel, byte dye)
    {
        var plate = TestDomainValues.Plate("Dyes", 20);
        var journal = new DiagnosticJournal();
        using var controller = Create(new(plate), journal: journal);

        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, targetMatches ? 20u : 21u, channel, dye),
            TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Equal(targetMatches ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.LocalDyeTarget).Observed);
        Assert.Equal(targetMatches ? (long?)(channel < 2 ? 1 : 0) : null,
            journal.Current.Checks.Single(check => check.Key == DiagnosticKey.DyeChannelSupported).Observed);
        Assert.Equal(dye == 0 ? 1 : 0, journal.Current.Checks.Single(check => check.Key == DiagnosticKey.DyeAvailable).Observed);
        Assert.Equal("This dye selection is no longer available. Select the item again.", controller.View.OperationFeedback);
        Assert.False(controller.View.HasUnsavedChanges);
    }

    /// <summary>A valid dye change records confirmation and switches back to the dyed preview.</summary>
    [Fact]
    public void AcceptedDyeConfirmsAndShowsSavedColours()
    {
        var plate = TestDomainValues.Plate("Dyes", 20);
        var journal = new DiagnosticJournal();
        using var controller = new MainController(new FakePlateLibraryStore(plate), new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), Facts(), journal, [new(7, "Red", 0, false, "")]);
        controller.Execute(new MainCommand.PreviewActive(PlateDyeMode.None), TestDomainValues.Timestamp, Guid.Empty);

        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 7), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(1, journal.Current.ConfirmedActions);
        Assert.Equal(PlateDyeMode.Saved, controller.View.PreviewDyeMode);
        Assert.Equal("Updated the draft dye choice. Use Save to keep it. Previewing uses no dye.", controller.View.Status);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
    }

    private static MainController Create(FakePlateLibraryStore store, FakePlateEditor? editor = null,
        FakeNativeCollectionValidator? native = null, FakeItemFacts? facts = null, DiagnosticJournal? journal = null)
        => new(store, native ?? new(), new FakeSharedCollectionSnapshotSource(), editor ?? new(), facts ?? Facts(), journal);
}
