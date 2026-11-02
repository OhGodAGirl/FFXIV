using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Transition tests observe state after subsequent commands rather than inspecting private fields.</summary>
public sealed class MainControllerTransitionTests
{
    /// <summary>Successful local work replaces an obsolete operation failure.</summary>
    [Theory]
    [InlineData("dye")]
    [InlineData("restore")]
    [InlineData("capture")]
    [InlineData("create")]
    public void SuccessfulActionClearsObsoleteFailure(string action)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var captured = PlateLibrary.Create("Captured", TestDomainValues.Timestamp, Guid.NewGuid());
        var editor = new FakePlateEditor { CaptureResult = new(PlateCaptureOutcome.Captured, captured) };
        using var controller = Create(new(plate), editor);
        controller.ReportOperationFailure(new MainCommand.LoadActive());
        MainCommand command = action switch
        {
            "dye" => new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0),
            "restore" => new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20),
            "capture" => new MainCommand.CaptureCurrentPlate(),
            _ => new MainCommand.CreatePlate(),
        };

        controller.Execute(command, TestDomainValues.Timestamp, Guid.NewGuid());

        Assert.Null(controller.View.OperationFeedback);
    }

    /// <summary>Saving an edited outfit removes its draft marker after the write succeeds.</summary>
    [Fact]
    public void SaveFirstOutfitClearsDirtyMarker()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), TestDomainValues.Timestamp, Guid.Empty);

        controller.Execute(new MainCommand.SaveOutfit(plate.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.False(controller.View.HasUnsavedChanges);
        Assert.False(Assert.Single(controller.View.SavedPlates).HasUnsavedChanges);
        Assert.Null(store.State.SavedPlates[0].Items[(int)PlateSlot.Body]);
    }

    /// <summary>Only a known plate may be armed for legacy delete confirmation.</summary>
    [Fact]
    public void DeleteArmTracksOnlyKnownIdentity()
    {
        var plate = TestDomainValues.Plate("A", 20);
        using var controller = Create(new(plate));
        controller.Execute(new MainCommand.BeginDelete(plate.Id), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(plate.Id, controller.View.DeleteArmedId);

        controller.Execute(new MainCommand.BeginDelete(Guid.NewGuid()), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Null(controller.View.DeleteArmedId);
    }

    /// <summary>Clearing all items or dyes marks the outfit as a changed draft.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WholeOutfitClearMarksDraft(bool items)
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 2;
        using var controller = Create(new(plate));
        var revision = controller.View.OutfitRevision;

        controller.Execute(items ? new MainCommand.ClearOutfitItems(plate.Id, revision)
            : new MainCommand.ClearOutfitDyes(plate.Id, revision), TestDomainValues.Timestamp, Guid.Empty);

        Assert.True(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.OutfitRevision > revision);
        Assert.Equal(0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
    }

    /// <summary>Main hand is a valid selectable and clearable slot, including index zero.</summary>
    [Fact]
    public void MainHandCanBeSelectedAndCleared()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[0] = new() { ItemId = 20 };
        using var controller = Create(new(plate));

        controller.Execute(new MainCommand.SelectSlot(PlateSlot.MainHand), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.MainHand), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(PlateSlot.MainHand, controller.View.SelectedSlot);
        Assert.Equal(0u, controller.View.Slots[0].ItemId);
    }

    /// <summary>Closing resets the draft and advances its binding revision.</summary>
    [Fact]
    public void ClosingRestoresSavedStateOnSubsequentCommand()
    {
        var plate = TestDomainValues.Plate("A", 20);
        using var controller = Create(new(plate));
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;

        controller.Execute(new MainCommand.CloseEditor(), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.PreviewActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(20u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.OutfitRevision > revision);
    }

    /// <summary>Delete updates list ordering and revision and forgets the removed draft.</summary>
    [Fact]
    public void DeletingDirtySelectionReindexesAndClearsItsDraft()
    {
        var a = TestDomainValues.Plate("A", 20);
        var b = PlateLibrary.Create("B", TestDomainValues.Timestamp, Guid.NewGuid());
        var store = new FakePlateLibraryStore(a, b);
        using var controller = Create(store);
        controller.Execute(new MainCommand.SetDye(a.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;

        controller.Execute(new MainCommand.DeletePlate(a.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0, Assert.Single(store.State.SavedPlates).LibraryOrder);
        Assert.True(controller.View.OutfitRevision > revision);
        Assert.False(controller.View.HasUnsavedChanges);
        var afterDelete = controller.View;
        controller.ResetUnsavedChanges();
        Assert.Same(afterDelete, controller.View);
        controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, a.Id);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(0u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
    }

    /// <summary>Adding a new outfit clears library search and invalidates old load bindings.</summary>
    [Fact]
    public void NewOutfitResetsSearchAndRevision()
    {
        using var controller = Create(new());
        controller.Execute(new MainCommand.SetPlateSearch("nothing"), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;

        controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, Guid.NewGuid());

        Assert.Equal(string.Empty, controller.View.PlateSearch);
        Assert.True(controller.View.OutfitRevision > revision);
        Assert.Single(controller.View.SavedPlates);
    }

    /// <summary>A closed editor does not perform a source lookup when adding a local item.</summary>
    [Fact]
    public void ClosedEditorAddsWithoutNativeSourceRead()
    {
        var native = new FakeNativeCollectionValidator();
        using var controller = Create(new(TestDomainValues.Plate("A")), new FakePlateEditor { Open = false }, native);

        controller.Execute(new MainCommand.AddItem(PlateSlot.Body, TestDomainValues.Item(20, "Item")), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0, native.CaptureCount);
        Assert.Equal(20u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
    }

    /// <summary>Failed source restoration retains explicit failure diagnostics and feedback.</summary>
    [Fact]
    public void MissingRestoreSourceReportsFailure()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var journal = new DiagnosticJournal();
        using var controller = Create(new(plate), native: new(), journal: journal);

        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Equal(0, Assert.Single(journal.Current.Checks).Observed);
        Assert.Equal("Stored dyes for this item are unavailable. The local outfit was not changed.", controller.View.OperationFeedback);
    }

    /// <summary>Native failure reporting records the exception outcome as well as readable feedback.</summary>
    [Fact]
    public void NativeFailureRetainsExceptionDiagnostics()
    {
        var journal = new DiagnosticJournal();
        using var controller = Create(new(), journal: journal);

        controller.ReportOperationFailure(new MainCommand.LoadActive(), TestDomainValues.Timestamp);

        Assert.Equal(DiagnosticStop.Exception, journal.Current.Stop);
        Assert.Equal(DiagnosticStop.Exception, journal.LastFailure!.Stop);
    }

    /// <summary>An invalid slot rejects the whole plan while retaining missing-item details for support.</summary>
    [Fact]
    public void InvalidAndMissingItemsRetainPerSlotEvidence()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 99 };
        var journal = new DiagnosticJournal();
        var native = new FakeNativeCollectionValidator { Snapshot = new([], true, true, TestDomainValues.Timestamp) };
        using var controller = Create(new(plate), native: native, journal: journal);

        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Contains(journal.Current.Checks, check => check.Key == DiagnosticKey.SlotCompatible && check.Slot == (int)PlateSlot.Head && check.Observed == 0);
        foreach (var key in new[] { DiagnosticKey.ItemId, DiagnosticKey.Dye0, DiagnosticKey.Dye1 })
            Assert.Contains(journal.Current.Checks, check => check.Key == key && check.Slot == (int)PlateSlot.Body && check.Observed is null);
        Assert.Contains("Head: item 99 is not valid for this slot.", controller.View.OperationFeedback);
        Assert.Contains("Body: item 20 is not available in the Dresser or Armoire.", controller.View.OperationFeedback);
        Assert.Contains("slot. Body:", controller.View.Status);
        Assert.Equal(2, journal.Current.Checks.Count(check => check.Key == DiagnosticKey.ExactSource && check.Slot == (int)PlateSlot.Body));
    }

    /// <summary>Native failures retain the actionable reason rather than only a loaded-item count.</summary>
    [Theory]
    [InlineData((int)PlateApplyFailureKind.PendingDyes, "Save or discard the current native dye changes before loading another outfit. No slots were changed.")]
    [InlineData((int)PlateApplyFailureKind.UnsupportedDye, "Body: this item does not support a saved dye channel. No slots were changed.")]
    [InlineData((int)PlateApplyFailureKind.DyeRejected, "Body: the game did not accept the dye change. Review the native plate before saving.")]
    public void NativeFailureCopyIsActionable(int kind, string reason)
    {
        var editor = new FakePlateEditor { ApplyResult = new(0, 0, [new((PlateApplyFailureKind)kind, PlateSlot.Body, 20)]) };
        using var controller = Create(new(TestDomainValues.Plate("A", 20)), editor);

        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal("Loaded 0 items. " + reason, controller.View.OperationFeedback);
    }

    /// <summary>Every position in the library can save a draft, not just its first entry.</summary>
    [Fact]
    public void SaveSecondOutfitPersistsDraft()
    {
        var a = TestDomainValues.Plate("A", 20);
        var b = PlateLibrary.Duplicate(a, TestDomainValues.Timestamp, Guid.NewGuid(), "B");
        var store = new FakePlateLibraryStore(a, b);
        using var controller = Create(store);
        controller.Execute(new MainCommand.SelectPlate(b.Id), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), TestDomainValues.Timestamp, Guid.Empty);

        controller.Execute(new MainCommand.SaveOutfit(b.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Null(store.State.SavedPlates[1].Items[(int)PlateSlot.Body]);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(0, controller.View.ActivePlate!.FilledSlotCount);
    }

    /// <summary>Reset without a dirty draft is an observation-only operation.</summary>
    [Fact]
    public void ResetWithoutChangesDoesNotRebuildOrInvalidate()
    {
        using var controller = Create(new(TestDomainValues.Plate("A", 20)));
        var before = controller.View;

        controller.ResetUnsavedChanges();

        Assert.Same(before, controller.View);
    }

    /// <summary>Fresh sources and failed or stale sources drive distinct footer attention states.</summary>
    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    public void SourceAttentionIsExplicit(bool dresserFresh, bool armoireFresh, bool failed, bool attention)
    {
        var shared = new FakeSharedCollectionSnapshotSource
        {
            Latest = TestDomainValues.SharedSnapshot(
            glamourDresser: dresserFresh ? CollectionSourceStatus.Fresh : CollectionSourceStatus.Stale,
            armoire: armoireFresh ? CollectionSourceStatus.Fresh : CollectionSourceStatus.Stale)
        };
        using var controller = new MainController(new FakePlateLibraryStore(), new FakeNativeCollectionValidator(),
            shared, new FakePlateEditor(), new FakeItemFacts());

        controller.UpdateSharedCollectionData(failed
            ? CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable)
            : CollectionDataParseResult.Accept(shared.Latest));

        Assert.True(controller.View.HasCollectionObservation);
        Assert.Equal(attention, controller.View.CollectionNeedsAttention);
        Assert.Equal("No known items match this slot and search.", controller.View.EmptyCollectionMessage);
    }

    /// <summary>A failed native load takes precedence over incidental preview feedback.</summary>
    [Fact]
    public void OperationFailureOutranksPreviewFeedback()
    {
        using var controller = Create(new());
        controller.ReportPreviewResult(false);

        controller.ReportOperationFailure(new MainCommand.LoadActive());
        controller.ReportPreviewResult(false);

        Assert.Contains("partially changed", controller.View.OperationFeedback);
    }

    /// <summary>Changing outfit or retrying its load clears missing-item marks when the native source returns.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingMarksClearOnSelectionOrSuccessfulRetry(bool select)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var native = new FakeNativeCollectionValidator { Snapshot = new([], true, true, TestDomainValues.Timestamp) };
        using var controller = Create(new(plate), native: native);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.True(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
        native.Snapshot = new([new(20, CollectionSource.Dresser, 0, 0, 0)], true, true, TestDomainValues.Timestamp);

        controller.Execute(select ? new MainCommand.SelectPlate(plate.Id) : new MainCommand.LoadActive(),
            TestDomainValues.Timestamp, Guid.Empty);

        Assert.False(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
    }

    /// <summary>Closing a dirty, unsuccessfully loaded draft restores the saved item without stale missing marks.</summary>
    [Fact]
    public void DirtyResetClearsMissingMarks()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var native = new FakeNativeCollectionValidator { Snapshot = new([], true, true, TestDomainValues.Timestamp) };
        using var controller = Create(new(plate), native: native);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        controller.ResetUnsavedChanges();

        Assert.False(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
        Assert.Null(controller.View.OperationFeedback);
    }

    /// <summary>The legacy stored-dye action uses the closest exact appearance and marks its draft.</summary>
    [Fact]
    public void StoredDyeActionPrefersExactAndMarksDraft()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 2;
        var native = new FakeNativeCollectionValidator
        {
            Snapshot = new([
            new(20, CollectionSource.Dresser, 0, 8, 0), new(20, CollectionSource.Dresser, 1, 2, 0)], true, true, TestDomainValues.Timestamp)
        };
        using var controller = Create(new(plate), native: native);

        controller.Execute(new MainCommand.UseStoredDyes(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(2, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.True(controller.View.HasUnsavedChanges);
        Assert.Equal("Updated the local outfit to its stored dyes. No dye was consumed.", controller.View.Status);
    }

    /// <summary>Stored-dye failure includes its failed source predicate and terminal reason.</summary>
    [Fact]
    public void StoredDyeActionRecordsSourceFailure()
    {
        var journal = new DiagnosticJournal();
        using var controller = Create(new(TestDomainValues.Plate("A", 20)), native: new(), journal: journal);

        controller.Execute(new MainCommand.UseStoredDyes(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Equal(DiagnosticKey.ExactSource, Assert.Single(journal.Current.Checks).Key);
        Assert.Equal(0, journal.Current.Checks[0].Observed);
    }

    /// <summary>A failed insertion preserves dirty items and missing-source evidence on the next render-state rebuild.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPersistentActionRestoresDraftAndMissingEvidence(bool delete)
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        var store = new FailingStore(plate);
        var facts = new FakeItemFacts(); facts.Masks[20] = TestDomainValues.AllSlots; facts.DyeCounts[20] = 2;
        var native = new FakeNativeCollectionValidator { Snapshot = new([], true, true, TestDomainValues.Timestamp) };
        using var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        var revision = controller.View.OutfitRevision;
        store.Fail = true;

        Assert.Throws<IOException>(() => controller.Execute(delete ? new MainCommand.DeletePlate(plate.Id)
            : new MainCommand.CreatePlate(), TestDomainValues.Timestamp, Guid.NewGuid()));
        controller.Execute(new MainCommand.PreviewActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.True(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
        Assert.Equal(plate.Id, controller.View.SelectedPlateId);
        Assert.Equal(revision, controller.View.OutfitRevision);
        Assert.Equal("Could not load the entire outfit - some pieces were missing.", controller.View.OperationFeedback);
        Assert.Equal(0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
    }

    /// <summary>A successful write remains authoritative if preparing the subsequent view fails.</summary>
    [Fact]
    public void ViewFailureAfterPersistenceDoesNotUndoSuccessfulWrite()
    {
        var plate = TestDomainValues.Plate("Before", 20);
        var facts = new FailingFacts();
        var store = new FailingStore(plate) { AfterSave = () => facts.Fail = true };
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts);

        Assert.Throws<InvalidOperationException>(() => controller.Execute(new MainCommand.RenamePlate(plate.Id, "After"),
            TestDomainValues.Timestamp, Guid.Empty));
        facts.Fail = false;
        controller.Execute(new MainCommand.PreviewActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal("After", Assert.Single(store.State.SavedPlates).Name);
        Assert.Equal("After", controller.View.ActivePlate!.Name);
    }

    /// <summary>Creating an outfit resets the previous dirty draft and persists an unambiguous ordered library.</summary>
    [Fact]
    public void CreateResetsPreviousDraftAndPersistsPositions()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), TestDomainValues.Timestamp, Guid.Empty);

        controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, Guid.NewGuid());

        Assert.All(controller.View.SavedPlates, summary => Assert.False(summary.HasUnsavedChanges));
        Assert.Equal(new int?[] { 0, 1 }, store.State.SavedPlates.Select(value => value.LibraryOrder));
    }

    /// <summary>A retained library can lose its selection; commands must handle that state without native writes.</summary>
    [Theory]
    [InlineData("select")]
    [InlineData("delete")]
    [InlineData("load")]
    [InlineData("clear")]
    public void MissingSelectionIsHandledWithoutNativeWrites(string action)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        using var controller = Create(store, editor);
        store.State.SelectedPlateId = null;
        MainCommand command = action switch
        {
            "select" => new MainCommand.SelectPlate(plate.Id),
            "delete" => new MainCommand.DeletePlate(plate.Id),
            "load" => new MainCommand.LoadActive(PlateId: plate.Id),
            _ => new MainCommand.ClearSlot(PlateSlot.Body),
        };

        controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0, editor.ApplyCount);
        if (action == "select") Assert.Equal(plate.Id, controller.View.SelectedPlateId);
        else Assert.Null(controller.View.SelectedPlateId);
        if (action == "delete") Assert.Empty(store.State.SavedPlates);
        else Assert.NotNull(store.State.SavedPlates[0].Items[(int)PlateSlot.Body]);
    }

    /// <summary>Restoring a valid item's stored dyes requires an open native editor.</summary>
    [Fact]
    public void RestoreWithClosedEditorDoesNotCaptureOrChangeDyes()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        var native = new FakeNativeCollectionValidator();
        using var controller = Create(new(plate), new FakePlateEditor { Open = false }, native);
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Contains("editor open", controller.View.OperationFeedback);
    }

    /// <summary>Zero item identifiers cannot become dye targets even when present in saved data.</summary>
    [Fact]
    public void ZeroItemCannotBecomeDyeTarget()
    {
        var plate = TestDomainValues.Plate("A", 0);
        plate.Items[(int)PlateSlot.Body] = new SavedPlateItem { ItemId = 0 };
        using var controller = Create(new(plate));
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 0, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Contains("no longer available", controller.View.OperationFeedback);
    }

    /// <summary>Malformed load modes and commands targeting empty slots remain harmless.</summary>
    [Theory]
    [InlineData("mode")]
    [InlineData("clear")]
    [InlineData("restore")]
    public void InvalidModeAndEmptySlotCommandsAreRejected(string action)
    {
        var plate = PlateLibrary.Create("Empty", TestDomainValues.Timestamp, Guid.NewGuid());
        var editor = new FakePlateEditor();
        using var controller = Create(new(plate), editor);
        MainCommand command = action switch
        {
            "mode" => new MainCommand.LoadActive(DyeMode: (PlateDyeMode)99),
            "clear" => new MainCommand.ClearSlot(PlateSlot.Body),
            _ => new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20),
        };
        controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(0, controller.View.ActivePlate!.FilledSlotCount);
    }

    /// <summary>Missing Examine names are rejected before their length is read.</summary>
    [Fact]
    public void MissingExamineNameIsRejected()
        => Assert.Null(BetterGlamorPlates.Features.Examine.ExaminedOutfitPolicy.Create(null!, [], TestDomainValues.Timestamp, Guid.NewGuid()));

    /// <summary>Restoring stored dyes creates a saveable draft and invalidates previous load bindings.</summary>
    [Fact]
    public void RestoreStoredDyesMarksDirtyAndAdvancesRevision()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        using var controller = Create(new(plate));
        var revision = controller.View.OutfitRevision;
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.True(controller.View.HasUnsavedChanges);
        Assert.True(controller.View.OutfitRevision > revision);
        Assert.Equal(0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
    }

    /// <summary>Unavailable collection data is not misreported as a missing exact item source.</summary>
    [Fact]
    public void UnavailableCollectionDoesNotInventSourceFailure()
    {
        var journal = new DiagnosticJournal();
        using var controller = Create(new(TestDomainValues.Plate("A", 20)),
            native: new FakeNativeCollectionValidator(), journal: journal);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.DoesNotContain(journal.Current.Checks, check => check.Key == DiagnosticKey.ExactSource);
    }

    /// <summary>Load errors identify a known item by its display name.</summary>
    [Fact]
    public void InvalidItemFailureUsesKnownName()
    {
        var facts = new FakeItemFacts();
        facts.Names[20] = "Known Robe";
        facts.Masks[20] = TestDomainValues.AllSlots with { Body = false };
        using var controller = new MainController(new FakePlateLibraryStore(TestDomainValues.Plate("A", 20)),
            new FakeNativeCollectionValidator { Snapshot = new([], true, true, TestDomainValues.Timestamp) },
            new FakeSharedCollectionSnapshotSource { Latest = TestDomainValues.SharedSnapshot(dresserItems: [20]) }, new FakePlateEditor(), facts);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Contains("Known Robe", controller.View.Status);
        Assert.DoesNotContain("still sees", controller.View.Status);
    }

    /// <summary>Editing an item invalidates the previous load's missing-item overlay.</summary>
    [Fact]
    public void EditingInvalidatesMissingItemOverlay()
    {
        var plate = TestDomainValues.Plate("A", 20);
        using var controller = Create(new(plate), native: new FakeNativeCollectionValidator
        { Snapshot = new([], true, true, TestDomainValues.Timestamp) });
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.True(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        Assert.False(controller.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
    }

    /// <summary>A failed save also rolls back edits made by a reentrant store callback.</summary>
    [Fact]
    public void FailedSaveRemovesReentrantDirtyState()
    {
        var plate = TestDomainValues.Plate("A", 20);
        MainController? controller = null;
        var store = new FailingStore(plate)
        {
            AfterSave = () =>
        {
            controller!.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
            throw new IOException("Synthetic callback failure");
        }
        };
        var facts = new FakeItemFacts(); facts.Masks[20] = TestDomainValues.AllSlots; facts.DyeCounts[20] = 2;
        using (controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts))
        {
            Assert.Throws<IOException>(() => controller.Execute(new MainCommand.RenamePlate(plate.Id, "After"), TestDomainValues.Timestamp, Guid.Empty));
            controller.Execute(new MainCommand.PreviewActive(), TestDomainValues.Timestamp, Guid.Empty);
            Assert.False(controller.View.HasUnsavedChanges);
            Assert.Equal("A", controller.View.ActivePlate!.Name);
        }
    }

    private sealed class FailingStore(SavedPlate plate) : IPlateLibraryStore
    {
        internal PlateLibraryState State { get; } = new([plate], plate.Id);
        internal bool Fail { get; set; }
        internal Action? AfterSave { get; init; }
        public PlateLibraryState Load() => State;
        public void Save(PlateLibraryState state)
        {
            if (Fail) throw new IOException("Synthetic write failure");
            AfterSave?.Invoke();
        }
    }

    private sealed class FailingFacts : IItemFacts
    {
        internal bool Fail { get; set; }
        public ItemDisplayFacts? Get(uint id)
            => Fail ? throw new InvalidOperationException("Synthetic view failure")
                : new("Item", 0, TestDomainValues.AllSlots);
    }

    private static MainController Create(FakePlateLibraryStore store, FakePlateEditor? editor = null,
        FakeNativeCollectionValidator? native = null, DiagnosticJournal? journal = null)
    {
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        facts.DyeCounts[20] = 2;
        native ??= new() { Snapshot = new([new(20, CollectionSource.Dresser, 0, 0, 0)], true, true, TestDomainValues.Timestamp) };
        return new(store, native, new FakeSharedCollectionSnapshotSource(), editor ?? new(), facts, journal);
    }
}
