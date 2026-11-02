using System.Text.RegularExpressions;

namespace SpringCleaning.Tests;

public sealed class ArchitectureContractTests
{
    private static readonly string Root = FindPluginRoot();

    [Fact]
    public void DecisionCodeDoesNotReferenceRuntimeBoundaries()
    {
        var decisionFiles = Directory
            .EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsDecisionFile)
            .ToArray();
        var forbidden = new[]
        {
            "using Dalamud",
            "Dalamud.",
            "ImGui.",
            "FFXIVClientStructs",
            "System.Net.Http",
            "System.IO.File",
            "System.IO.Directory",
            "DateTime.UtcNow",
            "GetIpcSubscriber",
        };

        Assert.NotEmpty(decisionFiles);
        foreach (var path in decisionFiles)
        {
            var source = File.ReadAllText(path);
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void UiDoesNotScanOrPersistAndDoesNotInvokeConsequentialNativeActions()
    {
        var uiFiles = Directory
            .EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Normalize(path).Contains("/Ui/", StringComparison.Ordinal))
            .ToArray();
        var forbidden = new[]
        {
            "SavePluginConfig(",
            "scanner.Scan",
            "GetExcelSheet",
            "GetInventoryContainer",
            "RequestDiscard(",
            "TryStore(",
            "TryRestore(",
            "ConfirmDresserItemStorage(",
        };

        Assert.NotEmpty(uiFiles);
        foreach (var path in uiFiles)
        {
            var source = File.ReadAllText(path);
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProductionUsesConstructorInjectionWithoutAPluginServiceLocator()
    {
        var pluginPath = Path.Combine(Root, "src", "Bootstrap", "Plugin.cs");
        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories).Where(IsOwnedSource))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("[PluginService]", source, StringComparison.Ordinal);
        }

        var pluginSource = File.ReadAllText(pluginPath);
        Assert.Contains("public Plugin(", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"\bstatic\s+I(?:DalamudPluginInterface|CommandManager|Framework|DataManager|GameGui|ObjectTable|TargetManager|ClientState|Condition|PluginLog|TextureProvider|AddonLifecycle)\b"),
            pluginSource);
    }

    [Fact]
    public void ProductionCodeHasNoHighConfidenceBlockingOrPlaceholderSmells()
    {
        var patterns = new[]
        {
            @"\basync\s+void\b",
            @"\.GetAwaiter\(\)\.GetResult\(\)",
            @"\.Result\b",
            @"\.Wait\s*\(",
            @"\bThread\.Sleep\s*\(",
            @"\b(?:TODO|FIXME)\b",
            @"_\s*=\s*Task\.",
        };

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories).Where(IsOwnedSource))
        {
            var source = File.ReadAllText(path);
            Assert.All(patterns, pattern =>
                Assert.DoesNotMatch(new Regex(pattern, RegexOptions.CultureInvariant), source));
        }
    }

    [Fact]
    public void RuntimeRegistrationsUseTheOwnedReverseTeardownRegistry()
    {
        var source = File.ReadAllText(Path.Combine(Root, "src", "Bootstrap", "Plugin.cs"));

        Assert.Contains("RuntimeRegistrationSet registrations", source, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Count(source, @"registrations\.Add\("));
        Assert.Contains("UiBuilder.HideUi -= ObserveUiHidden", source, StringComparison.Ordinal);
        Assert.Contains("UiBuilder.ShowUi -= ObserveUiShown", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseRegistrations();", source, StringComparison.Ordinal);
        Assert.Contains("if (disposed)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedPresentationDoesNotAuthorizeCommands()
    {
        var review = File.ReadAllText(Path.Combine(Root, "src", "Features", "Migration", "Ui", "MigrationWindow.cs"));
        Assert.Contains("safeCopies.Length == 0) && controller.HasFreshDuplicateOwnership)", review, StringComparison.Ordinal);
        Assert.Contains("!candidate.Safety.IsSafe) && controller.HasFreshDuplicateOwnership)", review, StringComparison.Ordinal);
        Assert.Contains("|| duplicateDiscardController.IsAnyRunning)\n                     && controller.HasFreshDuplicateOwnership)", review.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("ImGui.Selectable(label, false, ImGuiSelectableFlags.None, new Vector2(0, 28))\n            && controller.HasFreshDuplicateOwnership)", review.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("!footerEnabled.Value) && !disabled)", review, StringComparison.Ordinal);
        var overlay = File.ReadAllText(Path.Combine(Root, "src", "Features", "Dresser", "Ui", "DresserArmoireOverlayWindow.cs"));
        Assert.Contains("var canMove = controller.CanStart && candidates.Count != 0;", overlay, StringComparison.Ordinal);
        Assert.Contains("!moveButtonEnabled.Value) && canMove)", overlay, StringComparison.Ordinal);
        var dresser = File.ReadAllText(Path.Combine(Root, "src", "Features", "Dresser", "Ui", "DresserBookmarkWindow.cs"));
        Assert.Equal(3, Regex.Count(dresser, @"if \(clicked && canStart\)"));
        var drawer = File.ReadAllText(Path.Combine(Root, "src", "Features", "Migration", "Ui", "MigrationWindow.Drawer.cs"));
        Assert.Contains("var ready = controller.HasFreshDuplicateOwnership;", drawer, StringComparison.Ordinal);
        Assert.Contains("var presentationReady = ready || RetainPage;", drawer, StringComparison.Ordinal);
        Assert.Contains("presentationReady && !cleanup.IsRunning && !configuration.IgnoredDuplicateItemIds.Contains(copy.ItemId) && InventoryDuplicatePlanner.CanBulkDiscard(copy)", drawer, StringComparison.Ordinal);
        Assert.Contains("presentationReady && !cleanup.IsRunning && !IgnoredEntry(entry) && IsStoredEligible(entry)", drawer, StringComparison.Ordinal);
        Assert.Contains("&& controller.HasFreshDuplicateOwnership && !cleanup.IsRunning)", drawer, StringComparison.Ordinal);
        Assert.Contains("cleanup.IsRunning) && ready && !cleanup.IsRunning)", drawer, StringComparison.Ordinal);
        var bootstrap = File.ReadAllText(Path.Combine(Root, "src", "Bootstrap", "Plugin.cs"));
        Assert.Contains("LastFailure is not CollectionDataFailure.None and not CollectionDataFailure.StaleCompletion", bootstrap, StringComparison.Ordinal);
        var armoire = File.ReadAllText(Path.Combine(Root, "src", "Features", "Armoire", "Ui", "QuickStoreWindow.cs"));
        Assert.Contains("if (clicked && canStart)", armoire, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyDuplicateFieldIsRetainedWhileProtectionAndMissingOnlyStorageAreExplicit()
    {
        var configuration = File.ReadAllText(
            Path.Combine(Root, "src", "Bootstrap", "Configuration.cs"));
        var settings = File.ReadAllText(
            Path.Combine(Root, "src", "Shared", "Ui", "SettingsPanel.cs"));
        var controller = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Services", "MigrationController.cs"));
        var scanner = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Dresser", "Services", "DresserScanner.cs"));

        Assert.Contains("public bool SkipExistingDresserOutfits", configuration, StringComparison.Ordinal);
        Assert.Contains("Protect Items with Materia", settings, StringComparison.Ordinal);
        Assert.Contains(
            "configuration.ProtectItemsWithMateria = protectMateria",
            settings,
            StringComparison.Ordinal);
        Assert.DoesNotContain("configuration.SkipExistingDresserOutfits", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("skipExistingOutfits", scanner, StringComparison.Ordinal);
        Assert.Contains("CanStoreDresserItem(collectionSnapshot", scanner, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsDescriptionsStateTheirEffectsDirectly()
    {
        var settings = File.ReadAllText(
            Path.Combine(Root, "src", "Shared", "Ui", "SettingsPanel.cs"));

        Assert.Contains(
            "Store eligible pieces from incomplete outfits.",
            settings,
            StringComparison.Ordinal);
        Assert.Contains(
            "Disable to store complete outfits only.",
            settings,
            StringComparison.Ordinal);
        Assert.Contains(
            "Skips items that have materia when discarding duplicates.",
            settings,
            StringComparison.Ordinal);
        Assert.Contains("Skips items that have 100% Spiritbond when discarding duplicates", settings, StringComparison.Ordinal);
        Assert.Contains(
            "Remember the positions of Spring Cleaning and Glamour Creation windows.",
            settings,
            StringComparison.Ordinal);
        Assert.Contains(
            "Disable to use default positions.",
            settings,
            StringComparison.Ordinal);

        Assert.DoesNotContain("even if you do not have", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Disable this setting", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("already represented", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("pre-existing outfit glamour", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("positions you choose", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Turn this off", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewFilterIsPersistedAndBothDresserSourcesExcludeGearsetItems()
    {
        var configuration = File.ReadAllText(
            Path.Combine(Root, "src", "Bootstrap", "Configuration.cs"));
        var window = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Ui", "MigrationWindow.cs"));
        var scanner = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Dresser", "Services", "DresserScanner.cs"));
        var controller = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Services", "MigrationController.cs"));

        Assert.Contains("public bool ReviewOnlyItemsOutsideOutfits", configuration, StringComparison.Ordinal);
        Assert.Contains("Only Show Dresser Items Outside Outfits", window, StringComparison.Ordinal);
        var cache = File.ReadAllText(Path.Combine(Root, "src", "Features", "Inventory", "Policies", "DuplicateReviewCache.cs"));
        var drawer = File.ReadAllText(Path.Combine(Root, "src", "Features", "Migration", "Ui", "MigrationWindow.Drawer.cs"));
        Assert.Contains("ReviewPlanner.Plan(", cache, StringComparison.Ordinal);
        Assert.Contains("duplicateReviewCache.Get(", drawer, StringComparison.Ordinal);
        Assert.Contains("DresserAlertKind.NotInOutfit", scanner, StringComparison.Ordinal);
        Assert.Contains("const bool shouldHideGearsetItems = true;", controller, StringComparison.Ordinal);
        Assert.Contains("Excluding items registered to gear sets.", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Including inventory pieces registered to gear sets.", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DresserCompanionsRejectThePlateEditorWithoutChangingOperationalState()
    {
        var nativeUi = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Interop", "NativeUi.cs"));
        var plugin = File.ReadAllText(
            Path.Combine(Root, "src", "Bootstrap", "Plugin.cs"));

        Assert.Contains(
            "public bool IsDresserOpen => IsAddonVisible(\"MiragePrismPrismBox\")",
            nativeUi,
            StringComparison.Ordinal);
        Assert.Contains("DresserStationVisibilityPolicy.ShouldShowCompanions(", nativeUi, StringComparison.Ordinal);
        Assert.Contains("IsAddonVisible(\"MiragePrismMiragePlate\")", nativeUi, StringComparison.Ordinal);
        Assert.Contains("nativeUi.ShouldShowDresserCompanions", plugin, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomatedDresserStorageUsesNativeSelectionWithoutAContextMenu()
    {
        var controller = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Services", "MigrationController.cs"));

        Assert.Contains("var opened = nativeUi.OpenDresserStorage(item);", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("nativeUi.OpenInventoryContext(item)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectStoreInDresserMenuEntry", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCollectionDataIsTheSolePlanningAuthorityWithNativeActionRevalidation()
    {
        var controller = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Services", "MigrationController.cs"));
        var scanner = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Dresser", "Services", "DresserScanner.cs"));
        var plugin = File.ReadAllText(
            Path.Combine(Root, "src", "Bootstrap", "Plugin.cs"));
        var status = File.ReadAllText(
            Path.Combine(Root, "src", "Shared", "CollectionData", "CollectionDataStatusSnapshot.cs"));

        Assert.Contains("CollectionDataRefreshController collectionData", controller, StringComparison.Ordinal);
        Assert.Contains("collectionData.LatestSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("CollectionOwnershipPolicy", scanner, StringComparison.Ordinal);
        Assert.Contains("RevalidateEligibleItemsForAction", scanner, StringComparison.Ordinal);
        Assert.Contains("RevalidateOutfitDestination", scanner, StringComparison.Ordinal);
        Assert.Contains("collectionDataClient.TryTakeRefreshRequest(DateTimeOffset.UtcNow)", plugin, StringComparison.Ordinal);
        Assert.Contains(
            "Exportaramus supplies ownership; native UI revalidates every action.",
            status,
            StringComparison.Ordinal);

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories).Where(IsOwnedSource))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("CollectionExporter", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Collection Exporter", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NativeOutfitSelectionUsesDocumentedBoundsPolicy()
    {
        var nativeUi = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Migration", "Interop", "NativeUi.cs"));

        Assert.Contains(
            "OutfitSelectionBoundsPolicy.GetVisibleOutfitChoiceCount",
            nativeUi,
            StringComparison.Ordinal);
        Assert.Contains(
            "OutfitSelectionBoundsPolicy.GetSelectablePieceCount",
            nativeUi,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Math.Min((int)agent->Data->NumItemsInSet, 9)",
            nativeUi,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeWindowOcclusionRestoresTheFocusedStationAndMasksOtherWindowSurfaces()
    {
        var occlusion = File.ReadAllText(
            Path.Combine(Root, "src", "Shared", "Ui", "NativeWindowOcclusion.cs"));
        var focusTracker = File.ReadAllText(
            Path.Combine(Root, "src", "Shared", "Ui", "NativeWindowFocusTracker.cs"));
        var dresser = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Dresser", "Ui", "DresserBookmarkWindow.cs"));
        var armoire = File.ReadAllText(
            Path.Combine(Root, "src", "Features", "Armoire", "Ui", "QuickStoreWindow.cs"));
        var plugin = File.ReadAllText(
            Path.Combine(Root, "src", "Bootstrap", "Plugin.cs"));

        Assert.Contains(
            "NativeWindowOcclusionPolicy.ShouldOccludeNativeWindow",
            occlusion,
            StringComparison.Ordinal);
        Assert.Contains(
            "candidate->WindowHeaderCollisionNode",
            occlusion,
            StringComparison.Ordinal);
        Assert.Contains(
            "candidate->WindowNode",
            occlusion,
            StringComparison.Ordinal);
        Assert.Contains("anchorHasFocus", occlusion, StringComparison.Ordinal);
        Assert.Contains("AddonEvent.PostFocusChanged", focusTracker, StringComparison.Ordinal);
        Assert.Contains("GetUnitBaseForFocus", focusTracker, StringComparison.Ordinal);
        Assert.Contains("nativeWindowFocusTracker.IsFocused(dresser)", dresser, StringComparison.Ordinal);
        Assert.Contains("nativeWindowFocusTracker.IsFocused(addon)", armoire, StringComparison.Ordinal);
        Assert.Contains(
            "DisposeOwnedResource(nativeWindowFocusTracker.Dispose",
            plugin,
            StringComparison.Ordinal);
        Assert.Contains("DepthLayers", occlusion, StringComparison.Ordinal);
        Assert.Contains("anchorIndex", occlusion, StringComparison.Ordinal);
        Assert.DoesNotContain("if (candidate == focused)", occlusion, StringComparison.Ordinal);
        Assert.DoesNotContain("if (anchor == focused)", occlusion, StringComparison.Ordinal);
        Assert.DoesNotContain("manager == null || anchorHasFocus", occlusion, StringComparison.Ordinal);
    }

    // Pinned upstream files are validated separately from owned architecture rules.
    private static bool IsOwnedSource(string path) =>
        !Normalize(Path.GetRelativePath(Root, path)).StartsWith("src/Contracts/", StringComparison.Ordinal);

    private static bool IsDecisionFile(string path)
    {
        var relative = Normalize(Path.GetRelativePath(Root, path));
        return relative.Contains("/Policies/", StringComparison.Ordinal)
            || relative.Contains("/Controllers/", StringComparison.Ordinal)
            || relative.Contains("/Models/", StringComparison.Ordinal)
            || relative.Contains("/Shared/CollectionData/", StringComparison.Ordinal)
               && !relative.EndsWith("CollectionDataClient.cs", StringComparison.Ordinal)
            || relative.EndsWith("RuntimeRegistrationSet.cs", StringComparison.Ordinal)
            || relative.EndsWith("DiagnosticActivityLog.cs", StringComparison.Ordinal)
            || relative.EndsWith("DiagnosticVisibilityPolicy.cs", StringComparison.Ordinal)
            || relative.EndsWith("MigrationDiagnosticSnapshot.cs", StringComparison.Ordinal)
            || relative.EndsWith("StationIdentityPolicy.cs", StringComparison.Ordinal);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string FindPluginRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Spring Cleaning root.");
    }
}
