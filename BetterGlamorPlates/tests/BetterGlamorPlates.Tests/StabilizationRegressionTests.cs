using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Tests;

/// <summary>Feature-freeze regressions across draft, native boundary and lifecycle behavior.</summary>
public sealed class StabilizationRegressionTests
{
    /// <summary>A synchronous failed start still releases resources after reentrant disposal.</summary>
    [Fact]
    public async Task DisposeDuringThrowingStartReleasesOnce()
    {
        var errors = new List<Exception>();
        var releases = 0;
        var registry = new OwnedOperationRegistry(errors.Add);
        registry.AddDisposeWhenIdle(() => releases++);
        Assert.False(registry.TryStart(_ =>
        {
            registry.Dispose();
            throw new InvalidOperationException("start failed");
        }));
        await registry.WhenIdle.WaitAsync(TimeSpan.FromSeconds(1));
        registry.Dispose();
        Assert.Equal(1, releases);
        Assert.Equal(0, registry.ActiveCount);
        Assert.Single(errors);
    }

    /// <summary>Native clearing failures disclose possible partial native changes.</summary>
    [Fact]
    public void ClearItemsFailureUsesNativeWarning()
    {
        using var controller = Controller();
        controller.ReportOperationFailure(new MainCommand.ClearOutfitItems(Guid.Empty, 0));
        Assert.Contains("native editor may be partially changed", controller.View.OperationFeedback);
    }

    /// <summary>Closest source selection is stable across input permutations and source indices.</summary>
    [Fact]
    public void ClosestSourcePrefersDyesThenSourceThenLowestIndex()
    {
        var desired = new SavedPlateItem { ItemId = 20, Stain0Id = 2, Stain1Id = 3 };
        NativeCollectionItem[] items = [new(21, CollectionSource.Dresser, 0, 2, 3),
            new(20, CollectionSource.Armoire, 1, 2, 3), new(20, CollectionSource.Dresser, 9, 2, 3),
            new(20, CollectionSource.Dresser, 4, 2, 3), new(20, CollectionSource.Dresser, 0, 0, 0)];
        foreach (var rotation in Enumerable.Range(0, items.Length))
        {
            var values = items.Skip(rotation).Concat(items.Take(rotation));
            Assert.Equal(items[3], CollectionSourceResolver.ResolveClosest(values, desired));
        }
        Assert.Null(CollectionSourceResolver.ResolveClosest([], desired));
    }

    /// <summary>Null search means all saved outfits, retaining order.</summary>
    [Fact]
    public void NullSearchRetainsOutfits()
    {
        var plate = TestDomainValues.Plate("A", 20);
        Assert.Same(plate, Assert.Single(SavedPlateQuery.Apply([plate], null)));
    }

    /// <summary>Invalid screen dimensions cannot place UI off-screen.</summary>
    [Theory]
    [InlineData(8, 100)]
    [InlineData(100, 8)]
    public void TinyViewportRejectsCompanion(float width, float height)
        => Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, 40, 80), new(0, 0, width, height)));

    /// <summary>Non-finite collection dimensions still yield bounded usable cells.</summary>
    [Theory]
    [InlineData(float.NaN, 200)]
    [InlineData(200, float.PositiveInfinity)]
    public void InvalidGridDimensionsAreBounded(float width, float height)
    {
        var grid = MainLayoutPolicy.CollectionGrid(width, height, 4);
        Assert.InRange(grid.Columns, 1, 32);
        Assert.True(float.IsFinite(grid.TileSize));
        Assert.True(float.IsFinite(grid.RowPitch));
        Assert.True(grid.TileSize >= 1);
    }

    /// <summary>Every off-screen edge prevents the native Examine overlay.</summary>
    [Theory]
    [InlineData(-100, 0, 620, 960, 1920, 1080)]
    [InlineData(0, -1000, 620, 960, 1920, 1080)]
    [InlineData(1900, 0, 620, 960, 1920, 1080)]
    [InlineData(0, 500, 620, 960, 1920, 1080)]
    [InlineData(0, 0, 0, 960, 1920, 1080)]
    [InlineData(0, 0, 620, 0, 1920, 1080)]
    [InlineData(0, 0, 620, 960, 0, 1080)]
    [InlineData(0, 0, 620, 960, 1920, 0)]
    public void ExamineRejectsInvalidBounds(float x, float y, float w, float h, float vw, float vh)
        => Assert.Null(ExamineTabPlacement.Calculate(new(x, y, w, h), new(0, 0, vw, vh)));

    /// <summary>Stored dyes remain atomic if any item has no native source.</summary>
    [Fact]
    public void StoredDyesRejectMissingSourceWithoutPartialDraftEdit()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 9;
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 30, Stain0Id = 8 };
        var native = new FakeNativeCollectionValidator { Snapshot = new([new(20, CollectionSource.Dresser, 0, 2, 3)], true, true, TestDomainValues.Timestamp) };
        using var controller = Controller(plate, native: native);
        controller.Execute(new MainCommand.UseStoredDyes(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(8, controller.View.Slots[(int)PlateSlot.Head].Stain0Id);
        Assert.Contains("not changed", controller.View.Status);
    }

    /// <summary>Single-item restore uses stored colours only when the exact source exists.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoreStoredDyesHandlesSourcePresence(bool present)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var native = new FakeNativeCollectionValidator { Snapshot = new(present ? [new(20, CollectionSource.Dresser, 0, 2, 3)] : [], true, true, TestDomainValues.Timestamp) };
        using var controller = Controller(plate, native: native);
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(present ? 2 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(present ? 3 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
    }

    /// <summary>Failed persistence restores the library and draft before another save.</summary>
    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("icon")]
    [InlineData("create")]
    [InlineData("duplicate")]
    [InlineData("save")]
    public void FailedPersistenceCannotLeakIntoLaterSave(string action)
    {
        var plate = TestDomainValues.Plate("Original", 20);
        var store = new ThrowingStore(plate);
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), new FakeItemFacts(), isGameIcon: _ => true);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body, plate.Id, 20), TestDomainValues.Timestamp, Guid.Empty);
        var before = controller.View;
        MainCommand command = action switch
        {
            "delete" => new MainCommand.DeletePlate(plate.Id),
            "rename" => new MainCommand.RenamePlate(plate.Id, "Changed"),
            "icon" => new MainCommand.AssignIcon(plate.Id, 42),
            "create" => new MainCommand.CreatePlate(),
            "duplicate" => new MainCommand.DuplicatePlate(plate.Id),
            _ => new MainCommand.SaveOutfit(plate.Id),
        };
        store.Fail = true;
        Assert.Throws<IOException>(() => controller.Execute(command, TestDomainValues.Timestamp, Guid.NewGuid()));
        Assert.Same(before, controller.View);
        Assert.Equal("Original", Assert.Single(store.State.SavedPlates).Name);
        Assert.Equal(0u, store.State.SavedPlates[0].IconId);
        Assert.Equal(20u, store.State.SavedPlates[0].Items[(int)PlateSlot.Body]!.ItemId);
        store.Fail = false;
        controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, Guid.NewGuid());
        Assert.Equal("Original", store.State.SavedPlates[1].Name);
        Assert.Equal(20u, store.State.SavedPlates[1].Items[(int)PlateSlot.Body]!.ItemId);
    }

    /// <summary>Every native failure kind remains visible and does not persist the draft.</summary>
    [Theory]
    [InlineData((int)PlateApplyFailureKind.DyeUnavailable, 0)]
    [InlineData((int)PlateApplyFailureKind.DyeUnavailable, 7)]
    [InlineData((int)PlateApplyFailureKind.UnsupportedDye, 7)]
    [InlineData((int)PlateApplyFailureKind.DyeRejected, 7)]
    [InlineData((int)PlateApplyFailureKind.PendingDyes, 0)]
    public void NativeDyeFailuresAreReported(int kindValue, byte dyeId)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var editor = new FakePlateEditor { ApplyResult = new(0, 0, [new((PlateApplyFailureKind)kindValue, PlateSlot.Body, 20) { DyeId = dyeId }]) };
        var native = new FakeNativeCollectionValidator { Snapshot = new([new(20, CollectionSource.Dresser, 0, 0, 0)], true, true, TestDomainValues.Timestamp) };
        var facts = new FakeItemFacts(); facts.Masks[20] = TestDomainValues.AllSlots;
        var store = new FakePlateLibraryStore(plate);
        using var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.NotEmpty(controller.View.OperationFeedback!);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Cancellation callbacks ignore disposed controllers.</summary>
    [Fact]
    public void LoadCancellationUpdatesOnlyLiveController()
    {
        using var controller = Controller();
        controller.ReportLoadCancelled();
        Assert.Contains("cancelled", controller.View.OperationFeedback);
        var before = controller.View;
        controller.Dispose();
        controller.ReportLoadCancelled();
        controller.ImportExaminedOutfit(TestDomainValues.Plate("Ignored", 20));
        Assert.Same(before, controller.View);
    }

    /// <summary>Legacy restore actions cannot read a closed editor.</summary>
    [Fact]
    public void ClosedEditorRejectsStoredDyeActions()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var native = new FakeNativeCollectionValidator();
        using var controller = new MainController(new FakePlateLibraryStore(plate), native,
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor { Open = false }, new FakeItemFacts());
        controller.Execute(new MainCommand.UseStoredDyes(), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(0, native.CaptureCount);
        Assert.Contains("editor open", controller.View.OperationFeedback);
    }

    /// <summary>Oversized Examine lists are refused before creating a local outfit.</summary>
    [Fact]
    public void ExamineListIsBounded()
        => Assert.Null(ExaminedOutfitPolicy.Create("Name", Enumerable.Repeat(new ExaminedAppearance(0, 0, 0, 0, 0, 0, 0), 15).ToArray(), TestDomainValues.Timestamp, Guid.NewGuid()));

    /// <summary>Unsupported preview enum values cannot create native plans.</summary>
    [Fact]
    public void InvalidDyeModeRejectsPlan()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PlateLoadPlanner.Create(TestDomainValues.Plate("A", 20),
            NativeCollectionSnapshot.Empty, (_, _) => true, (PlateDyeMode)99));

    /// <summary>Offline user journey crosses draft, storage, reload and validated native-plan boundaries.</summary>
    [Fact]
    public void SavedOutfitJourneyResetsEditsThenLoadsOnlyAvailableItems()
    {
        var id = Guid.NewGuid();
        var store = new FakePlateLibraryStore();
        var native = new FakeNativeCollectionValidator { Snapshot = new([new(20, CollectionSource.Dresser, 0, 0, 0)], true, true, TestDomainValues.Timestamp) };
        var editor = new FakePlateEditor();
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        facts.Masks[30] = TestDomainValues.AllSlots;
        using (var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts))
        {
            controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, id);
            controller.Execute(new MainCommand.AddItem(PlateSlot.Body, TestDomainValues.Item(20, "Body")), TestDomainValues.Timestamp, Guid.Empty);
            controller.Execute(new MainCommand.AddItem(PlateSlot.Head, TestDomainValues.Item(30, "Head")), TestDomainValues.Timestamp, Guid.Empty);
            controller.Execute(new MainCommand.SaveOutfit(id), TestDomainValues.Timestamp, Guid.Empty);
            controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body, id, 20), TestDomainValues.Timestamp, Guid.Empty);
            Assert.False(controller.View.Slots[(int)PlateSlot.Body].HasItem);
            controller.Execute(new MainCommand.CloseEditor(), TestDomainValues.Timestamp, Guid.Empty);
            Assert.True(controller.View.Slots[(int)PlateSlot.Body].HasItem);
        }
        using var reopened = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts);
        var savesBeforeLoad = store.SaveCount;
        reopened.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(12, editor.LastPlan!.Steps.Count);
        Assert.True(editor.LastPlan.Steps.Single(step => step.Slot == PlateSlot.Head).IsSkipped);
        Assert.False(editor.LastPlan.Steps.Single(step => step.Slot == PlateSlot.Body).IsSkipped);
        Assert.True(reopened.View.Slots[(int)PlateSlot.Head].MissingFromLastLoad);
        Assert.False(reopened.View.Slots[(int)PlateSlot.Body].MissingFromLastLoad);
        Assert.Equal(savesBeforeLoad, store.SaveCount);
    }

    /// <summary>Malformed slots and capabilities cannot mutate the active outfit.</summary>
    [Theory]
    [InlineData(-1, 20, 2)]
    [InlineData(12, 20, 2)]
    [InlineData(2, 20, 2)]
    [InlineData(3, 20, 3)]
    [InlineData(3, 0, 2)]
    public void DyeTargetsRequireValidItemAndCapabilities(int slot, uint expected, byte channels)
    {
        var plate = TestDomainValues.Plate("A", 20);
        var store = new FakePlateLibraryStore(plate);
        var facts = new FakeItemFacts(); facts.Masks[20] = TestDomainValues.AllSlots; facts.DyeCounts[20] = channels;
        using var controller = new MainController(store, new FakeNativeCollectionValidator(), new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts);
        controller.Execute(new MainCommand.SetDye(plate.Id, (PlateSlot)slot, expected, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(0, store.SaveCount);
        Assert.Contains("no longer available", controller.View.OperationFeedback);
    }

    /// <summary>Commands bound to absent outfits or invalid confirmation IDs have no side effects.</summary>
    [Fact]
    public void AbsentOutfitCommandsDoNotMutateOrReadNativeState()
    {
        var store = new FakePlateLibraryStore();
        var native = new FakeNativeCollectionValidator();
        var editor = new FakePlateEditor();
        using var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, new FakeItemFacts());
        var missing = Guid.NewGuid();
        MainCommand[] commands = [new MainCommand.BeginDelete(missing), new MainCommand.ConfirmDelete(missing),
            new MainCommand.ClearOutfitItems(missing, 0), new MainCommand.ClearOutfitDyes(missing, 0),
            new MainCommand.ClearItemDyes(missing, PlateSlot.Body, 20), new MainCommand.UseStoredDyes(),
            new MainCommand.LoadActive(PlateDyeMode.Saved, missing, 0)];
        foreach (var command in commands) controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);
        Assert.Empty(store.State.SavedPlates);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(0, native.CaptureCount);
    }

    /// <summary>An explicitly valid icon can be cleared back to the outfit-derived icon.</summary>
    [Fact]
    public void IconAssignmentCanResetToOutfitDefault()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.IconId = 42;
        using var controller = new MainController(new FakePlateLibraryStore(plate), new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), new FakeItemFacts(), isGameIcon: _ => true);
        controller.Execute(new MainCommand.AssignIcon(plate.Id, 0), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(0u, controller.View.ActivePlate!.IconId);
    }

    private sealed class ThrowingStore(SavedPlate plate) : IPlateLibraryStore
    {
        internal PlateLibraryState State { get; } = new([plate], plate.Id);
        internal bool Fail { get; set; }
        public PlateLibraryState Load() => State;
        public void Save(PlateLibraryState state)
        {
            if (Fail) throw new IOException("Storage unavailable");
        }
    }

    private static MainController Controller(SavedPlate? plate = null, FakeNativeCollectionValidator? native = null)
        => new(new FakePlateLibraryStore(plate is null ? [] : [plate]), native ?? new(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), new FakeItemFacts());
}
