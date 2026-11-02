namespace BetterGlamorPlates.Tests.Features.Main;

using System.Reflection;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;

/// <summary>
/// Verifies deliberate UI commands are interpreted by controller policy before
/// persistence or native mechanisms are invoked.
/// </summary>
public sealed class MainControllerTests
{
    /// <summary>Verifies library commands persist only through the owned store.</summary>
    [Fact]
    public void LibraryCommandsPersistThroughTheOwnedStore()
    {
        var store = new FakePlateLibraryStore();
        using var controller = CreateController(store);
        var createdId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        controller.Execute(new MainCommand.CreatePlate(), TestDomainValues.Timestamp, createdId);
        Assert.Equal("Plate 1", Assert.Single(store.State.SavedPlates).Name);
        Assert.Equal("Created local plate Plate 1.", controller.View.Status);
        controller.Execute(
            new MainCommand.RenameActive("  Raid Plate  "),
            TestDomainValues.Timestamp.AddMinutes(1),
            Guid.Empty);

        var plate = Assert.Single(store.State.SavedPlates);
        Assert.Equal(createdId, plate.Id);
        Assert.Equal("Raid Plate", plate.Name);
        Assert.Equal(TestDomainValues.Timestamp.AddMinutes(1), controller.View.ActivePlate!.UpdatedAtUtc);
        Assert.Equal(2, store.SaveCount);
        Assert.Equal("Raid Plate", Assert.Single(controller.View.SavedPlates).Name);
        Assert.Equal("Renamed the local plate to Raid Plate.", controller.View.Status);
    }

    /// <summary>Verifies invalid policy results never reach native mechanisms.</summary>
    [Fact]
    public void InvalidPlanNeverReachesNativeApplyMechanism()
    {
        var plate = TestDomainValues.Plate("Invalid", 100);
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot(
                [TestDomainValues.NativeItem(100)],
                true,
                true,
                TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        var facts = new FakeItemFacts();
        facts.Names[100] = "Wrong slot";
        facts.Masks[100] = default;
        using var controller = new MainController(
            store,
            source,
            new FakeSharedCollectionSnapshotSource(),
            editor,
            facts);

        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(0, editor.ApplyCount);
        Assert.Contains("not valid", controller.View.Status, StringComparison.Ordinal);
        Assert.Equal(1, source.CaptureCount);
    }

    /// <summary>Verifies a complete validated plan is applied exactly once.</summary>
    [Fact]
    public void ValidPlanReachesNativeMechanismExactlyOnce()
    {
        var plate = TestDomainValues.Plate("Ready", 100);
        var store = new FakePlateLibraryStore(plate);
        var item = TestDomainValues.NativeItem(100);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot(
                [item],
                true,
                true,
                TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor
        {
            ApplyResult = new PlateApplyResult(1, 11, []),
        };
        var facts = new FakeItemFacts();
        facts.Names[100] = "Ready item";
        facts.Masks[100] = TestDomainValues.AllSlots;
        using var controller = new MainController(
            store,
            source,
            new FakeSharedCollectionSnapshotSource(),
            editor,
            facts);

        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(1, editor.ApplyCount);
        Assert.NotNull(editor.LastPlan);
        Assert.Equal(PlateRules.SlotCount, editor.LastPlan.Steps.Count);
        Assert.Equal("Loaded 1 items and cleared 11 slots.", controller.View.OperationFeedback);
        Assert.Equal(
            "Loaded 1 items and cleared 11 slots.",
            controller.View.Status);
    }

    /// <summary>Verifies capture failure produces no persisted state.</summary>
    [Fact]
    public void CaptureFailureDoesNotPersistOrCreateAPlate()
    {
        var store = new FakePlateLibraryStore();
        var editor = new FakePlateEditor();
        using var controller = CreateController(store, editor: editor);

        var captureId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        controller.Execute(
            new MainCommand.CaptureCurrentPlate(),
            TestDomainValues.Timestamp,
            captureId);

        Assert.Equal(1, editor.CaptureCount);
        Assert.Equal("Plate 1", editor.LastCaptureName);
        Assert.Equal(TestDomainValues.Timestamp, editor.LastCaptureTimestamp);
        Assert.Equal(captureId, editor.LastCapturePlateId);
        Assert.Empty(store.State.SavedPlates);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(
            "Open the Glamour Plates editor and select a plate first.",
            controller.View.Status);
    }

    /// <summary>Verifies stale commands cannot mutate a disposed controller.</summary>
    [Fact]
    public void DisposedControllerRejectsStaleQueuedCommands()
    {
        var store = new FakePlateLibraryStore();
        var controller = CreateController(store);
        controller.Dispose();

        controller.Execute(
            new MainCommand.CreatePlate(),
            TestDomainValues.Timestamp,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.Empty(store.State.SavedPlates);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Verifies constructor selection repair and command guards.</summary>
    [Fact]
    public void ConstructorRepairsSelectionAndGuardsInvalidCommands()
    {
        var plate = TestDomainValues.Plate("Only");
        var store = new FakePlateLibraryStore(plate);
        store.State.SelectedPlateId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        using var controller = CreateController(store);

        Assert.Equal(plate.Id, controller.View.SelectedPlateId);
        Assert.Throws<ArgumentNullException>(() => controller.Execute(
            null!, TestDomainValues.Timestamp, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Execute(
            new UnknownCommand(), TestDomainValues.Timestamp, Guid.Empty));

        controller.Dispose();
        controller.Dispose();

        var emptyStore = new FakePlateLibraryStore();
        using var emptyController = CreateController(emptyStore);
        Assert.Null(emptyController.View.SelectedPlateId);
    }

    /// <summary>Verifies a valid selection among peers is persisted exactly once.</summary>
    [Fact]
    public void SelectPlateChoosesTheRequestedPeerAndPersists()
    {
        var first = TestDomainValues.Plate("First");
        var second = TestDomainValues.Plate("Second");
        second.Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var store = new FakePlateLibraryStore(first, second);
        using var controller = CreateController(store);

        controller.Execute(
            new MainCommand.SelectPlate(second.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(second.Id, controller.View.SelectedPlateId);
        Assert.Equal(1, store.SaveCount);
    }

    /// <summary>Verifies construction repairs shapes and builds exact initial render state.</summary>
    [Fact]
    public void ConstructionBuildsCanonicalOrderedViewState()
    {
        var older = TestDomainValues.Plate("Older", 10);
        older.Items = [new SavedPlateItem { ItemId = 10 }];
        older.UpdatedAtUtc = TestDomainValues.Timestamp;
        var newer = TestDomainValues.Plate("Newer", 20);
        newer.Id = Guid.NewGuid();
        newer.UpdatedAtUtc = TestDomainValues.Timestamp.AddMinutes(1);
        var store = new FakePlateLibraryStore(older, newer);
        var facts = new FakeItemFacts();
        facts.Names[10] = "Visible item";
        facts.Icons[10] = 456;
        using var controller = CreateController(store, facts: facts);

        Assert.Equal(PlateRules.SlotCount, older.Items.Length);
        Assert.Equal(
            "Select a saved plate or create a new one.",
            controller.View.Status);
        Assert.Equal("Collection not available yet.", controller.View.SharedCollectionStatus);
        Assert.Equal(
            "Exportaramus is unavailable. Ownership remains unknown; Better Glamour Plates will not treat it as an empty collection.",
            controller.View.SharedCollectionDiagnostics.Guidance);
        Assert.Equal("Not checked yet.", controller.View.SharedCollectionDiagnostics.GlamourDresser.Detail);
        Assert.Equal("Not checked yet.", controller.View.SharedCollectionDiagnostics.Armoire.Detail);
        Assert.Equal(["Newer", "Older"], controller.View.SavedPlates.Select(plate => plate.Name));
        Assert.Equal("Visible item", controller.View.Slots[0].ItemName);
        Assert.Equal("Main Hand", controller.View.Slots[0].SlotName);
        Assert.Equal(456u, controller.View.Slots[0].IconId);
        Assert.Equal(SharedOwnershipState.Unknown, controller.View.Slots[0].SharedOwnership);
        Assert.Equal("Unknown", controller.View.Slots[0].SharedOwnershipLabel);
        Assert.True(controller.View.Slots[0].HasItem);
        Assert.Equal("Empty", controller.View.Slots[1].ItemName);
        Assert.False(controller.View.Slots[1].HasItem);
    }

    /// <summary>Verifies unknown item facts use a stable player-visible fallback name.</summary>
    [Fact]
    public void ConstructionFallsBackToNumericItemNameWhenFactsAreUnknown()
    {
        var plate = TestDomainValues.Plate("Unknown item", 777);
        using var controller = CreateController(new FakePlateLibraryStore(plate));

        Assert.Equal("item 777", controller.View.Slots[(int)PlateSlot.Body].ItemName);
    }

    /// <summary>Verifies the default empty filter exposes refreshed compatible items.</summary>
    [Fact]
    public void DefaultFilterDoesNotHideRefreshedItems()
    {
        var item = TestDomainValues.Item(10, "Visible item");
        var source = new FakeNativeCollectionValidator();
        var facts = new FakeItemFacts();
        facts.Names[10] = item.Name;
        facts.Masks[10] = TestDomainValues.AllSlots;
        facts.Icons[10] = item.IconId;
        var shared = TestDomainValues.SharedSnapshot(dresserItems: [10], armoireItems: []);
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = CreateController(
            new FakePlateLibraryStore(),
            source,
            facts: facts,
            sharedSource: sharedSource);
        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));

        controller.Execute(
            new MainCommand.RefreshCollection(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(item, Assert.Single(controller.View.VisibleItems));
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal("Showing 1 collected item(s).", controller.View.Status);
    }

    /// <summary>Verifies view refresh never performs native ownership scanning.</summary>
    [Fact]
    public void RefreshCollectionUsesOnlyExportaramusOwnership()
    {
        var source = new FakeNativeCollectionValidator();
        using var controller = CreateController(new FakePlateLibraryStore(), source);

        controller.Execute(
            new MainCommand.RefreshCollection(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(0, source.CaptureCount);
        Assert.Equal("Collection not available yet.", controller.View.Status);
    }

    /// <summary>Verifies accepted shared data is the sole collection-view authority.</summary>
    [Fact]
    public void AcceptedSharedCollectionDataDrivesCollectionView()
    {
        var item = TestDomainValues.Item(61, "Exportaramus item");
        var source = new FakeNativeCollectionValidator();
        var facts = new FakeItemFacts();
        facts.Names[61] = item.Name;
        facts.Masks[61] = TestDomainValues.AllSlots;
        facts.Icons[61] = item.IconId;
        var shared = TestDomainValues.SharedSnapshot();
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = CreateController(
            new FakePlateLibraryStore(),
            source,
            facts: facts,
            sharedSource: sharedSource);

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));
        controller.Execute(
            new MainCommand.RefreshCollection(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal("Collection ready.", controller.View.SharedCollectionStatus);
        Assert.Contains("ownership authority", controller.View.SharedCollectionDiagnostics.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Current", controller.View.SharedCollectionDiagnostics.GlamourDresser.ProviderStatus);
        Assert.Equal(item, Assert.Single(controller.View.VisibleItems, candidate => candidate.ItemId == 61));
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal("Showing 3 collected item(s).", controller.View.Status);
    }

    /// <summary>Verifies failed shared refreshes can retain the last accepted passive snapshot.</summary>
    [Fact]
    public void SharedCollectionFailuresRetainPassiveDiagnosticsWhenSafe()
    {
        var shared = TestDomainValues.SharedSnapshot(providerVersion: "0.3.1.0");
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = CreateController(
            new FakePlateLibraryStore(),
            sharedSource: sharedSource);

        controller.UpdateSharedCollectionData(
            CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable));

        Assert.Equal(
            "Collection ready. Refresh failed; keeping saved ownership.",
            controller.View.SharedCollectionStatus);
        Assert.Contains("Last refresh failed", controller.View.SharedCollectionDiagnostics.Guidance, StringComparison.Ordinal);
        Assert.Contains("provider unavailable", controller.View.SharedCollectionDiagnostics.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies unknown provider sources remain unknown and do not trigger native scans.</summary>
    [Fact]
    public void RefreshCollectionPreservesUnknownProviderSources()
    {
        var source = new FakeNativeCollectionValidator();
        var shared = TestDomainValues.SharedSnapshot(
            glamourDresser: CollectionSourceStatus.Unavailable,
            armoire: CollectionSourceStatus.NeverObserved,
            dresserItems: [],
            armoireItems: []);
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = CreateController(
            new FakePlateLibraryStore(),
            source,
            sharedSource: sharedSource);

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));
        controller.Execute(
            new MainCommand.RefreshCollection(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(0, source.CaptureCount);
        Assert.Empty(controller.View.VisibleItems);
        Assert.Equal("Showing 0 collected item(s).", controller.View.Status);
        Assert.Equal("Unavailable", controller.View.SharedCollectionDiagnostics.GlamourDresser.ProviderStatus);
        Assert.Equal("Never observed", controller.View.SharedCollectionDiagnostics.Armoire.ProviderStatus);
    }

    /// <summary>Verifies shared data never substitutes for a fresh native exact-source scan.</summary>
    [Fact]
    public void LoadActiveIgnoresSharedCollectionDataForNativeValidation()
    {
        var plate = TestDomainValues.Plate("Native only", 100);
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot([], true, true, TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        var facts = new FakeItemFacts();
        facts.Names[100] = "Missing natively";
        facts.Masks[100] = TestDomainValues.AllSlots;
        var shared = TestDomainValues.SharedSnapshot();
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = new MainController(store, source, sharedSource, editor, facts);

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));
        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(1, source.CaptureCount);
        Assert.Contains(editor.LastPlan!.Steps, step => step.IsSkipped && step.Source is null);
        Assert.Equal("Could not load the entire outfit - some pieces were missing.", controller.View.Status);
    }

    /// <summary>Verifies passive shared ownership hints do not bypass native exact-source rejection.</summary>
    [Fact]
    public void LoadActiveSkipsNativeGapDespitePassiveFreshHint()
    {
        var plate = TestDomainValues.Plate("Passive hint", 61);
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot([], true, true, TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        var facts = new FakeItemFacts();
        facts.Names[61] = "Shared only";
        facts.Masks[61] = TestDomainValues.AllSlots;
        var shared = TestDomainValues.SharedSnapshot();
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = new MainController(store, source, sharedSource, editor, facts);

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));
        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(1, source.CaptureCount);
        Assert.Contains(editor.LastPlan!.Steps, step => step.IsSkipped && step.Source is null);
        Assert.Equal("Could not load the entire outfit - some pieces were missing.", controller.View.Status);
        Assert.DoesNotContain("Exportaramus", controller.View.Status, StringComparison.Ordinal);
    }

    /// <summary>Verifies one hint-eligible issue among mixed validation failures still supplies passive context.</summary>
    [Fact]
    public void LoadActiveAddsPassiveHintForMixedIssueKinds()
    {
        var plate = TestDomainValues.Plate("Mixed", 61);
        plate.Items[(int)PlateSlot.Head] = new SavedPlateItem { ItemId = 100 };
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot([], true, true, TestDomainValues.Timestamp),
        };
        var facts = new FakeItemFacts();
        facts.Names[61] = "Shared only";
        facts.Names[100] = "Wrong slot";
        facts.Masks[61] = TestDomainValues.AllSlots;
        facts.Masks[100] = default;
        var shared = TestDomainValues.SharedSnapshot();
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = new MainController(
            store,
            source,
            sharedSource,
            new FakePlateEditor(),
            facts);
        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));

        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Contains("not valid", controller.View.Status, StringComparison.Ordinal);
        Assert.Contains("Dresser or Armoire", controller.View.Status, StringComparison.Ordinal);
        Assert.Contains("Exportaramus still sees 1 matching item ID(s)", controller.View.Status, StringComparison.Ordinal);
    }

    /// <summary>Verifies capture success, selection, duplication, and deletion policy.</summary>
    [Fact]
    public void LibraryLifecycleCommandsPreserveSelectionAndConfirmation()
    {
        var original = TestDomainValues.Plate("Original", 10);
        var store = new FakePlateLibraryStore(original);
        var capturedPlate = TestDomainValues.Plate("Captured");
        capturedPlate.Items = [new SavedPlateItem { ItemId = 20 }];
        var editor = new FakePlateEditor
        {
            CaptureResult = new PlateCaptureResult(
                PlateCaptureOutcome.Captured,
                capturedPlate),
        };
        var facts = new FakeItemFacts();
        facts.Names[10] = "Original item";
        facts.Names[20] = "Captured item";
        using var controller = CreateController(store, editor: editor, facts: facts);

        controller.Execute(
            new MainCommand.CaptureCurrentPlate(),
            TestDomainValues.Timestamp,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var captured = store.State.SavedPlates.Single(plate => plate.Name == "Captured");
        Assert.Equal(PlateRules.SlotCount, captured.Items.Length);
        Assert.Contains("Saved 1 slots", controller.View.Status, StringComparison.Ordinal);

        controller.Execute(
            new MainCommand.SelectPlate(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(captured.Id, controller.View.SelectedPlateId);

        controller.Execute(
            new MainCommand.SelectPlate(original.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(original.Id, controller.View.SelectedPlateId);
        var savesAfterSelection = store.SaveCount;
        controller.Execute(
            new MainCommand.DuplicateActive(),
            TestDomainValues.Timestamp.AddMinutes(1),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var duplicate = store.State.SavedPlates.Single(plate => plate.Name == "Original Copy");
        Assert.Equal(duplicate.Id, controller.View.SelectedPlateId);
        Assert.Equal(savesAfterSelection + 1, store.SaveCount);
        Assert.Equal("Created local plate Original Copy.", controller.View.Status);

        controller.Execute(
            new MainCommand.ArmOrDelete(original.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(original.Id, controller.View.DeleteArmedId);
        controller.Execute(
            new MainCommand.ArmOrDelete(duplicate.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(duplicate.Id, controller.View.DeleteArmedId);
        Assert.Equal(
            "Select Confirm Delete to remove Original Copy from the local library.",
            controller.View.Status);
        var savesBeforeDelete = store.SaveCount;
        controller.Execute(
            new MainCommand.ArmOrDelete(duplicate.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.DoesNotContain(store.State.SavedPlates, plate => plate.Id == duplicate.Id);
        Assert.Equal(original.Id, controller.View.SelectedPlateId);
        Assert.Equal(savesBeforeDelete + 1, store.SaveCount);
        Assert.Equal(
            "Deleted the local saved plate. The native FFXIV plates were unchanged.",
            controller.View.Status);

        controller.Execute(
            new MainCommand.ArmOrDelete(original.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.ArmOrDelete(original.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(captured.Id, controller.View.SelectedPlateId);
        controller.Execute(
            new MainCommand.ArmOrDelete(captured.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.ArmOrDelete(captured.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Null(controller.View.SelectedPlateId);

        controller.Execute(
            new MainCommand.RenameActive("ignored"),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.DuplicateActive(),
            TestDomainValues.Timestamp,
            Guid.NewGuid());
        controller.Execute(
            new MainCommand.ArmOrDelete(original.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
    }

    /// <summary>Verifies slot selection, filtering, clear, and add policy branches.</summary>
    [Fact]
    public void SlotEditingCommandsValidateActivePlateAndCompatibility()
    {
        var plate = TestDomainValues.Plate("Editable", 10);
        var store = new FakePlateLibraryStore(plate);
        var facts = new FakeItemFacts();
        facts.Names[10] = "Existing";
        facts.Names[20] = "Compatible";
        using var controller = CreateController(store, facts: facts);

        controller.Execute(
            new MainCommand.ArmOrDelete(plate.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.SelectSlot(PlateSlot.Head),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(PlateSlot.Head, controller.View.SelectedSlot);
        Assert.Null(controller.View.DeleteArmedId);

        controller.Execute(
            new MainCommand.SetFilter("needle"),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.AddItem(
                PlateSlot.Head,
                TestDomainValues.Item(20, "Wrong", mask: default(EquipSlotMask))),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Null(plate.Items[(int)PlateSlot.Head]);
        controller.Execute(
            new MainCommand.AddItem(
                PlateSlot.Head,
                TestDomainValues.Item(20, "Compatible")),
            TestDomainValues.Timestamp.AddMinutes(1),
            Guid.Empty);
        Assert.Equal(20u, controller.View.Slots[(int)PlateSlot.Head].ItemId);
        Assert.Equal(TestDomainValues.Timestamp.AddMinutes(1), controller.View.ActivePlate!.UpdatedAtUtc);
        Assert.Contains("Added Compatible", controller.View.Status, StringComparison.Ordinal);

        controller.Execute(
            new MainCommand.ClearSlot(PlateSlot.Head),
            TestDomainValues.Timestamp.AddMinutes(2),
            Guid.Empty);
        Assert.Null(plate.Items[(int)PlateSlot.Head]);
        Assert.Equal(TestDomainValues.Timestamp.AddMinutes(2), controller.View.ActivePlate!.UpdatedAtUtc);
        Assert.Equal("Cleared Head in the local plate.", controller.View.Status);

        store.State.SelectedPlateId = null;
        controller.Execute(
            new MainCommand.ClearSlot(PlateSlot.Body),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.AddItem(
                PlateSlot.Body,
                TestDomainValues.Item(30, "Ignored")),
            TestDomainValues.Timestamp,
            Guid.Empty);
    }

    /// <summary>Verifies every validation issue and native failure description.</summary>
    [Fact]
    public void LoadCommandsDescribeEverySafeFailureOutcome()
    {
        var noPlateStore = new FakePlateLibraryStore();
        using (var noPlate = CreateController(noPlateStore))
        {
            noPlate.Execute(
                new MainCommand.LoadActive(),
                TestDomainValues.Timestamp,
                Guid.Empty);
        }

        var plate = TestDomainValues.Plate("Load", 100);
        plate.Items[(int)PlateSlot.Head] = new SavedPlateItem { ItemId = 200 };
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot([], false, true, TestDomainValues.Timestamp),
        };
        var facts = new FakeItemFacts();
        facts.Names[100] = "Missing";
        facts.Names[200] = "Also missing";
        facts.Masks[100] = TestDomainValues.AllSlots;
        facts.Masks[200] = TestDomainValues.AllSlots;
        var editor = new FakePlateEditor();
        using var controller = new MainController(
            store,
            source,
            new FakeSharedCollectionSnapshotSource(),
            editor,
            facts);

        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Contains("Wait for both", controller.View.Status, StringComparison.Ordinal);

        source.Snapshot = new NativeCollectionSnapshot([], true, true, TestDomainValues.Timestamp);
        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal("Could not load the entire outfit - some pieces were missing.", controller.View.Status);
        Assert.Equal(2, controller.View.Slots.Count(slot => slot.MissingFromLastLoad));

        source.Snapshot = new NativeCollectionSnapshot(
            [
                TestDomainValues.NativeItem(100),
                TestDomainValues.NativeItem(200),
            ],
            true,
            true,
            TestDomainValues.Timestamp);
        editor.ApplyResult = new PlateApplyResult(
            1,
            2,
            [
                new PlateApplyFailure(PlateApplyFailureKind.EditorNotReady),
                new PlateApplyFailure(PlateApplyFailureKind.EditorClosed),
                new PlateApplyFailure(PlateApplyFailureKind.ClearRejected, PlateSlot.Head),
                new PlateApplyFailure(PlateApplyFailureKind.ItemRejected, PlateSlot.Body, 100),
                new PlateApplyFailure(PlateApplyFailureKind.EditorClosedAfterPartial),
                new PlateApplyFailure((PlateApplyFailureKind)999),
            ]);
        controller.Execute(
            new MainCommand.LoadActive(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Contains("not ready", controller.View.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("did not clear", controller.View.Status, StringComparison.Ordinal);
        var describe = typeof(MainController).GetMethod("DescribeFailure", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var failure in editor.ApplyResult.Failures)
            Assert.False(string.IsNullOrWhiteSpace((string?)describe.Invoke(controller, [failure])));

        var method = typeof(MainController).GetMethod(
            "DescribeIssue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var fallback = Assert.IsType<string>(method!.Invoke(
            controller,
            [new PlateLoadIssue((PlateLoadIssueKind)999)]));
        Assert.Equal("The local plate could not be validated.", fallback);

        var getItemName = typeof(MainController).GetMethod(
            "GetItemName",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("item 999", getItemName!.Invoke(controller, [999U]));
    }

    /// <summary>Verifies clearing shared state removes passive diagnostics.</summary>
    [Fact]
    public void ClearSharedCollectionDataRemovesPassiveDiagnostics()
    {
        var shared = TestDomainValues.SharedSnapshot();
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        using var controller = CreateController(
            new FakePlateLibraryStore(),
            sharedSource: sharedSource);
        controller.UpdateSharedCollectionData(CollectionDataParseResult.Accept(shared));

        sharedSource.Latest = null;
        controller.ClearSharedCollectionData();

        Assert.Equal("Collection not available yet.", controller.View.SharedCollectionStatus);
        Assert.Equal(SharedCollectionDiagnostics.Empty, controller.View.SharedCollectionDiagnostics);
    }

    /// <summary>Verifies rail search and collection source toggles remain pure view state.</summary>
    [Fact]
    public void SearchAndSourceCommandsRecomposeTheViewWithoutPersisting()
    {
        var raid = TestDomainValues.Plate("Raid", 10);
        var crafting = TestDomainValues.Plate("Crafting", 11);
        var store = new FakePlateLibraryStore(raid, crafting);
        var shared = TestDomainValues.SharedSnapshot(
            dresserItems: [100],
            armoireItems: [200]);
        var sharedSource = new FakeSharedCollectionSnapshotSource { Latest = shared };
        var facts = new FakeItemFacts();
        facts.Names[100] = "Dresser Item";
        facts.Names[200] = "Armoire Item";
        facts.Masks[100] = TestDomainValues.AllSlots;
        facts.Masks[200] = TestDomainValues.AllSlots;
        using var controller = CreateController(store, facts: facts, sharedSource: sharedSource);

        controller.Execute(
            new MainCommand.SetPlateSearch(" craft "),
            TestDomainValues.Timestamp,
            Guid.Empty);
        controller.Execute(
            new MainCommand.ToggleCollectionSource(CollectionSource.Dresser),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Equal("Crafting", Assert.Single(controller.View.SavedPlates).Name);
        Assert.Equal(2, controller.View.TotalSavedPlateCount);
        Assert.Equal(CollectionSourceFilter.All, controller.View.CollectionSources);
        Assert.Equal(2, controller.View.VisibleItems.Count);
        Assert.Equal(0, store.SaveCount);

        controller.Execute(
            new MainCommand.ToggleCollectionSource(CollectionSource.Armoire),
            TestDomainValues.Timestamp,
            Guid.Empty);
        Assert.Equal(CollectionSourceFilter.All, controller.View.CollectionSources);
        Assert.Equal(2, controller.View.VisibleItems.Count);
    }

    /// <summary>Verifies the single primary action explains both disabled states.</summary>
    [Fact]
    public void PrimaryLoadAvailabilityRequiresASelectionAndOpenNativeEditor()
    {
        var closedEditor = new FakePlateEditor { Open = false };
        using var noPlate = CreateController(new FakePlateLibraryStore(), editor: closedEditor);
        Assert.False(noPlate.View.CanLoad);
        Assert.Equal("Select or create a saved plate first.", noPlate.View.LoadDisabledReason);

        using var closed = CreateController(
            new FakePlateLibraryStore(TestDomainValues.Plate("Saved", 12)),
            editor: closedEditor);
        Assert.False(closed.View.CanLoad);
        Assert.Equal("Open the FFXIV Glamour Plates editor first.", closed.View.LoadDisabledReason);

        using var open = CreateController(
            new FakePlateLibraryStore(TestDomainValues.Plate("Saved", 13)),
            editor: new FakePlateEditor { Open = true });
        Assert.True(open.View.CanLoad);
        Assert.Null(open.View.LoadDisabledReason);
    }

    /// <summary>Verifies dismissing the actions menu clears only delete confirmation state.</summary>
    [Fact]
    public void DisarmDeleteClearsConfirmationWithoutPersisting()
    {
        var plate = TestDomainValues.Plate("Saved", 14);
        var store = new FakePlateLibraryStore(plate);
        using var controller = CreateController(store);
        controller.Execute(
            new MainCommand.ArmOrDelete(plate.Id),
            TestDomainValues.Timestamp,
            Guid.Empty);
        var savesBefore = store.SaveCount;

        controller.Execute(
            new MainCommand.DisarmDelete(),
            TestDomainValues.Timestamp,
            Guid.Empty);

        Assert.Null(controller.View.DeleteArmedId);
        Assert.Equal(savesBefore, store.SaveCount);
        Assert.Single(store.State.SavedPlates);
    }

    /// <summary>Verifies every shared-data failure maps to concise player-facing status.</summary>
    [Theory]
    [MemberData(nameof(SharedCollectionFailures))]
    public void SharedCollectionFailuresAreDescribedExactly(
        int failureValue,
        string expected)
    {
        using var controller = CreateController(new FakePlateLibraryStore());
        var failure = (CollectionDataFailure)failureValue;

        controller.UpdateSharedCollectionData(
            failure == CollectionDataFailure.None
                ? default
                : CollectionDataParseResult.Reject(failure));

        Assert.Equal(expected, controller.View.SharedCollectionStatus);
    }

    /// <summary>Provides the exact player-facing shared-data failure messages.</summary>
    public static TheoryData<int, string> SharedCollectionFailures =>
        new()
        {
            { (int)CollectionDataFailure.NoCharacter, "Collection will be available after logging in." },
            { (int)CollectionDataFailure.ProviderUnavailable, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.EmptyPayload, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.PayloadTooLarge, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.MalformedJson, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.UnsupportedProtocol, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.InvalidProvider, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.WrongCharacter, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.InvalidRevision, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.OutOfOrder, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.InvalidTimestamp, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.InvalidSources, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.Busy, "Collection is refreshing." },
            { (int)CollectionDataFailure.Disposed, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.StaleCompletion, "Exportaramus is unavailable; ownership is unknown." },
            { (int)CollectionDataFailure.None, "Exportaramus is unavailable; ownership is unknown." },
        };

    /// <summary>Preview feedback does not commit the outfit to the native editor.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreviewFeedbackLeavesNativePlateUntouched(bool accepted)
    {
        var editor = new FakePlateEditor();
        using var controller = CreateController(new FakePlateLibraryStore(), editor: editor);
        controller.ReportPreviewResult(accepted);
        Assert.Contains("native plate is unchanged", controller.View.Status);
        Assert.Contains(accepted ? "Previewing" : "not ready", controller.View.Status);
    }
    /// <summary>Selecting a dyed item preserves both source stains and produces an executable exact plan.</summary>
    [Fact]
    public void SelectingStoredDyedItemCanLoadWithoutDyeConsumption()
    {
        var plate = TestDomainValues.Plate("Dyes");
        var store = new FakePlateLibraryStore(plate);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot(
                [new NativeCollectionItem(20, CollectionSource.Dresser, 4, 7, 9)], true, true, TestDomainValues.Timestamp),
        };
        var facts = new FakeItemFacts();
        facts.Names[20] = "Dyed item";
        facts.Masks[20] = TestDomainValues.Item(20, "Dyed item").SlotMask;
        var editor = new FakePlateEditor();
        using var controller = CreateController(store, source, editor, facts);
        controller.Execute(new MainCommand.AddItem(PlateSlot.Body, TestDomainValues.Item(20, "Dyed item")),
            TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(0, editor.ApplyCount);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(7, editor.LastPlan!.Steps[(int)PlateSlot.Body].Source!.Stain0Id);
        Assert.Equal(9, editor.LastPlan.Steps[(int)PlateSlot.Body].Source!.Stain1Id);
    }

    /// <summary>Existing dye mismatches can be deliberately repaired without loading the native plate.</summary>
    [Fact]
    public void StoredDyeRepairIsExplicitAndDoesNotLoad()
    {
        var plate = TestDomainValues.Plate("Repair", 20);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot(
                [new NativeCollectionItem(20, CollectionSource.Dresser, 4, 7, 9)], true, true, TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        using var controller = CreateController(new FakePlateLibraryStore(plate), source, editor);
        controller.Execute(new MainCommand.UseStoredDyes(), TestDomainValues.Timestamp, Guid.Empty);
        var item = Assert.Single(controller.View.Slots, item => item.HasItem);
        Assert.Equal(7, item.Stain0Id);
        Assert.Equal(9, item.Stain1Id);
        Assert.Equal(0, editor.ApplyCount);
    }
    /// <summary>Saving another character's look creates a new persisted entry without replacing a namesake.</summary>
    [Fact]
    public void ExaminedOutfitImportPersistsAndKeepsExistingPlate()
    {
        var existing = TestDomainValues.Plate("Redna Liorasch's Glamour", 10);
        var store = new FakePlateLibraryStore(existing);
        var editor = new FakePlateEditor();
        using var controller = CreateController(store, editor: editor);
        var imported = TestDomainValues.Plate("Redna Liorasch's Glamour", 20);
        imported.Id = Guid.NewGuid();
        controller.ImportExaminedOutfit(imported);
        Assert.Equal(2, store.State.SavedPlates.Count);
        Assert.Contains(store.State.SavedPlates, value => ReferenceEquals(existing, value));
        Assert.Equal(imported.Id, controller.View.SelectedPlateId);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
    }
    /// <summary>Duplicates stay beside their source across draft saves and library reloads.</summary>
    [Fact]
    public void DuplicateStaysAboveOriginalAfterSaveAndReload()
    {
        var first = TestDomainValues.Plate("First", 1);
        var original = TestDomainValues.Plate("Original", 2);
        original.Id = Guid.NewGuid();
        first.LibraryOrder = 0;
        original.LibraryOrder = 1;
        var store = new FakePlateLibraryStore(first, original);
        var copyId = Guid.NewGuid();
        using (var controller = CreateController(store))
        {
            controller.Execute(new MainCommand.DuplicatePlate(original.Id), TestDomainValues.Timestamp, copyId);
            Assert.Equal(new[] { first.Id, copyId, original.Id }, controller.View.SavedPlates.Select(plate => plate.Id));
            controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), TestDomainValues.Timestamp, Guid.Empty);
            controller.Execute(new MainCommand.SaveOutfit(copyId), TestDomainValues.Timestamp, Guid.Empty);
        }
        using var reloaded = CreateController(store);
        Assert.Equal(new[] { first.Id, copyId, original.Id }, reloaded.View.SavedPlates.Select(plate => plate.Id));
        reloaded.Execute(new MainCommand.DeletePlate(copyId), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(new[] { first.Id, original.Id }, reloaded.View.SavedPlates.Select(plate => plate.Id));
        Assert.DoesNotContain(store.State.SavedPlates, plate => plate.Id == copyId);
    }

    private sealed record UnknownCommand : MainCommand;

    /// <summary>Copyable evidence retains a missing native source without exposing the private plate name.</summary>
    [Fact]
    public void FailedLoadRetainsMissingSourceWithoutPrivatePlateName()
    {
        var plate = TestDomainValues.Plate("Private Player's Glamour", 20);
        var source = new FakeNativeCollectionValidator
        {
            Snapshot = new NativeCollectionSnapshot([new(21, CollectionSource.Dresser, 4, 7, 9)],
                true, true, TestDomainValues.Timestamp),
        };
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        var editor = new FakePlateEditor();
        var journal = new DiagnosticJournal();
        using var controller = new MainController(new FakePlateLibraryStore(plate), source,
            new FakeSharedCollectionSnapshotSource(), editor, facts, journal);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Contains(editor.LastPlan!.Steps, step => step.IsSkipped && step.SavedItem!.ItemId == 20);
        Assert.Contains(journal.Current.Checks, check => check.Key == DiagnosticKey.ExactSource && check.Observed == 0);
        Assert.Contains(controller.View.Slots, slot => slot.ItemId == 20 && slot.MissingFromLastLoad);
        controller.ClearSharedCollectionData();
        Assert.DoesNotContain("Private Player", journal.ToJson(DateTimeOffset.UtcNow, null));
    }

    private static MainController CreateController(
        FakePlateLibraryStore store,
        FakeNativeCollectionValidator? source = null,
        FakePlateEditor? editor = null,
        FakeItemFacts? facts = null,
        FakeSharedCollectionSnapshotSource? sharedSource = null)
        => new(
            store,
            source ?? new FakeNativeCollectionValidator(),
            sharedSource ?? new FakeSharedCollectionSnapshotSource(),
            editor ?? new FakePlateEditor(),
            facts ?? new FakeItemFacts());
}









