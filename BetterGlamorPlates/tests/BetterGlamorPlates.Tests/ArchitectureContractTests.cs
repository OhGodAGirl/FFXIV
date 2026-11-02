using System.Text.RegularExpressions;

namespace BetterGlamorPlates.Tests;

/// <summary>
/// Enforces Better Glamour Plates' actual policy, UI, adapter, and ownership
/// boundaries. Text scans are a backstop to the fake-backed behavior tests.
/// </summary>
public sealed class ArchitectureContractTests
{
    private static readonly string Root = FindPluginRoot();

    /// <summary>Verifies pure policy files remain independent from runtime boundaries.</summary>
    [Fact]
    public void DeterministicPolicyFilesDoNotReferenceRuntimeBoundaries()
    {
        string[] relativePaths =
        [
            "src/Features/GlamourPlates/CollectionSourceResolver.cs",
            "src/Features/GlamourPlates/CollectionProviderPolicy.cs",
            "src/Features/GlamourPlates/CollectionViewPlanner.cs",
            "src/Features/GlamourPlates/PlateLibrary.cs",
            "src/Features/GlamourPlates/PlateLoadPlanner.cs",
            "src/Features/GlamourPlates/PlateRules.cs",
            "src/Features/GlamourPlates/SavedPlateQuery.cs",
            "src/Features/Main/MainController.cs",
            "src/Features/Main/MainLayoutPolicy.cs",
            "src/Features/Main/CompanionPlacementPolicy.cs",
            "src/Features/Main/EquipmentSummaryPolicy.cs",
            "src/Features/GlamourPlates/PlateResultPolicy.cs",
            "src/Features/GlamourPlates/ExportaramusProjection.cs",
        ];
        string[] forbidden =
        [
            "Dalamud",
            "ImGui",
            "FFXIVClientStructs",
            "Lumina",
            "System.IO",
            "System.Net",
            "DateTime.UtcNow",
            "Guid.NewGuid",
        ];

        foreach (var relative in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(Root, relative));
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    /// <summary>Verifies the window renders state and emits commands only.</summary>
    [Fact]
    public void WindowOnlyRendersStateAndEmitsCommands()
    {
        var source = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, "src/Features/Main"),
            "MainWindow*.cs").Select(File.ReadAllText));
        string[] forbidden =
        [
            "GetExcelSheet",
            "GetAddonByName",
            "FFXIVClientStructs",
            "SavePluginConfig",
            "PlateLibrary.",
            "NativePlateEditor.Load",
            "DateTime.UtcNow",
            "Guid.NewGuid",
            "configuration.SavedPlates",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        Assert.Contains("Action<MainCommand>", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies the native adapter applies plans without owning policy.</summary>
    [Fact]
    public void NativeAdapterDoesNotOwnPlanningOrCollectionPolicy()
    {
        var source = Read("src/Features/GlamourPlates/NativePlateEditor.cs");
        Assert.DoesNotContain("PlateLoadPlanner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionScanner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SavePluginConfig", source, StringComparison.Ordinal);
        Assert.Contains("Apply(PlateLoadPlan plan)", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies injected services and registrations have explicit ownership.</summary>
    [Fact]
    public void PluginHasNoMutableStaticServiceLocatorAndSymmetricSubscriptions()
    {
        var source = Read("src/Bootstrap/Plugin.cs");
        Assert.DoesNotContain("[PluginService]", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"static\s+I(?:Dalamud|Command|Framework|Data|Game|Client|Texture|Plugin)", RegexOptions.CultureInvariant),
            source);
        Assert.Contains("framework.Update += OnFrameworkUpdate", source, StringComparison.Ordinal);
        Assert.Contains("framework.Update -= OnFrameworkUpdate", source, StringComparison.Ordinal);
        Assert.Contains("UiBuilder.Draw += windowSystem.Draw", source, StringComparison.Ordinal);
        Assert.Contains("UiBuilder.Draw -= windowSystem.Draw", source, StringComparison.Ordinal);
        Assert.Contains("RemoveHandler(PluginIdentity.CommandName)", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies the recurring framework callback never scans native collections.</summary>
    [Fact]
    public void FrameworkUpdateDoesNotPerformRecurringCollectionWork()
    {
        var source = Read("src/Bootstrap/Plugin.cs");
        var updateStart = source.IndexOf(
            "private void OnFrameworkUpdate",
            StringComparison.Ordinal);
        var queueStart = source.IndexOf(
            "private void SaveExaminedOutfit",
            updateStart,
            StringComparison.Ordinal);
        var updateBody = source[updateStart..queueStart];

        Assert.DoesNotContain("Scan(", updateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCollection", updateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("RunOnTick", updateBody, StringComparison.Ordinal);
        Assert.Contains("nativeEditor.IsOpen", updateBody, StringComparison.Ordinal);
    }

    /// <summary>Verifies shared snapshot invoke and parse stay off the framework thread.</summary>
    [Fact]
    public void SharedCollectionParsingRunsOffFrameworkThread()
    {
        var source = Read("src/Bootstrap/Plugin.cs");
        var start = source.IndexOf("collections = new CollectionCoordinator", StringComparison.Ordinal);
        var end = source.IndexOf("commandExecution = new CommandExecutionCoordinator", start, StringComparison.Ordinal);
        var composition = source[start..end];
        Assert.Contains("Task.Run(() => collectionDataClient.RefreshAsync", composition, StringComparison.Ordinal);
        Assert.Contains("framework.RunOnTick(action", composition, StringComparison.Ordinal);

        // Static boundary backstop; CollectionCoordinatorTests prove stale/cancelled results cannot publish.
        var coordinator = Read("src/Bootstrap/CollectionCoordinator.cs");
        var refresh = coordinator.IndexOf("await refreshData(", StringComparison.Ordinal);
        var publish = coordinator.IndexOf("await schedule(", StringComparison.Ordinal);
        Assert.True(refresh >= 0);
        Assert.True(publish > refresh);
    }

    /// <summary>Verifies operation start callbacks execute outside the registry lock.</summary>
    [Fact]
    public void OperationRegistryDoesNotInvokeSchedulersWhileHoldingItsGate()
    {
        var source = Read("src/Shared/Operations/OwnedOperationRegistry.cs");
        var startBegin = source.IndexOf(
            "internal bool TryStart",
            StringComparison.Ordinal);
        var disposeBegin = source.IndexOf(
            "public void Dispose()",
            startBegin,
            StringComparison.Ordinal);
        var startBody = source[startBegin..disposeBegin];
        var firstLock = startBody.IndexOf("lock (gate)", StringComparison.Ordinal);
        var firstLockClose = startBody.IndexOf("        }", firstLock, StringComparison.Ordinal);
        var firstStartCall = startBody.IndexOf("start(token)", StringComparison.Ordinal);

        Assert.True(firstLock >= 0, "Expected TryStart to guard active ownership.");
        Assert.True(firstLockClose > firstLock, "Expected the first TryStart lock to close before scheduler invocation.");
        Assert.True(firstStartCall >= 0, "Expected TryStart to invoke the scheduler.");
        Assert.True(firstStartCall > firstLockClose, "Expected start(token) to run after the first registry lock closed.");
    }

    /// <summary>Verifies only load/apply policy performs one-shot native validation.</summary>
    [Fact]
    public void LoadActiveUsesOneShotNativeValidationOnlyAtActionTime()
    {
        var source = Read("src/Features/Main/MainController.cs");
        var loadStart = source.IndexOf(
            "private void LoadActive",
            StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void ClearSlot",
            loadStart,
            StringComparison.Ordinal);
        var loadBody = source[loadStart..nextMethod];

        Assert.Contains("nativeSnapshot = nativeCollectionValidator.Capture(capturedAtUtc);", loadBody, StringComparison.Ordinal);
        Assert.Contains("PlateLoadPlanner.Create(", loadBody, StringComparison.Ordinal);
        Assert.Contains("nativeSnapshot", loadBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionViewPlanner", loadBody, StringComparison.Ordinal);

        var refreshStart = source.IndexOf("private void RefreshCollection", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void CaptureCurrent", refreshStart, StringComparison.Ordinal);
        var refreshBody = source[refreshStart..refreshEnd];
        Assert.DoesNotContain("nativeCollectionValidator", refreshBody, StringComparison.Ordinal);
    }

    /// <summary>Verifies Exportaramus is the only reusable collection-ownership source.</summary>
    [Fact]
    public void CollectionOwnershipHasOneCanonicalSourceAndNoLegacyExporter()
    {
        var sourceFiles = Directory.EnumerateFiles(
                Path.Combine(Root, "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .ToArray();
        var combined = string.Join('\n', sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("CollectionExporter", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Collection Exporter", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ICollectionSnapshotSource", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("class CollectionScanner", combined, StringComparison.Ordinal);

        foreach (var path in sourceFiles.Where(path =>
                     !path.EndsWith("NativeCollectionValidator.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("PrismBoxItemIds", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IsItemInCabinet", source, StringComparison.Ordinal);
        }

        var controller = Read("src/Features/Main/MainController.cs");
        Assert.DoesNotContain("CollectionDataSnapshot? sharedCollectionSnapshot", controller, StringComparison.Ordinal);
        Assert.Contains("sharedCollectionSource.Latest", controller, StringComparison.Ordinal);

        var window = Read("src/Features/Main/MainWindow.cs");
        Assert.DoesNotContain("CollectionProviderPolicy", window, StringComparison.Ordinal);
    }

    /// <summary>Verifies production source contains no blocking or placeholder smells.</summary>
    [Fact]
    public void ProductionCodeHasNoHighConfidenceBlockingOrPlaceholderSmells()
    {
        string[] patterns =
        [
            @"\basync\s+void\b",
            @"\.GetAwaiter\(\)\.GetResult\(\)",
            @"\.Result\b",
            @"\.Wait\s*\(",
            @"\bThread\.Sleep\s*\(",
            @"\b(?:TODO|FIXME)\b",
            @"\b_\s*=\s*[^;]*Task",
        ];

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            Assert.All(patterns, pattern =>
                Assert.DoesNotMatch(new Regex(pattern, RegexOptions.CultureInvariant), source));
        }
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(Root, relative));

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

        throw new DirectoryNotFoundException("Could not locate the plugin root.");
    }
}


