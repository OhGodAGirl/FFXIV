using System.Text.RegularExpressions;

namespace CollectionShopper.Tests;

public sealed class ArchitectureContractTests
{
    [Fact]
    public void ShopperDoesNotRecalculateGlobalMouseCaptureWithoutNativeOcclusion()
    {
        var source = Read("src/Features/Shopping/ShopperWindow.cs").Replace("\r\n", "\n");
        Assert.Contains("if (Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs))\n"
            + "            ImGuiP.UpdateHoveredWindowAndCaptureFlags();", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("ImGuiP.UpdateHoveredWindowAndCaptureFlags();", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DiagnosticCaptureUsesManagedObservationsAndTheLoadedModule()
    {
        var source = Read("src/Bootstrap/Plugin.cs");
        var start = source.IndexOf("private DiagnosticSnapshot CaptureDiagnostics()", StringComparison.Ordinal);
        var end = source.IndexOf("private string GetInstallChannel()", start, StringComparison.Ordinal);
        var capture = source[start..end];
        Assert.DoesNotContain("GetAddonByName", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadBundled", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("Instance()", capture, StringComparison.Ordinal);
        Assert.Contains("Assembly.ManifestModule.ModuleVersionId", capture, StringComparison.Ordinal);
        Assert.Contains("wasMarketBoardOpen", capture, StringComparison.Ordinal);
    }
    private static readonly string Root = FindPluginRoot();

    [Fact]
    public void BothPricePublicationPathsRecheckCurrentWorld()
    {
        var source = Read("src/Features/Shopping/ShopperWindow.cs");
        Assert.Equal(2, Regex.Count(source, @"MarketPricePublication.Validate\(market,"));
    }

    [Fact]
    public void SettingsDisabledScopesAlwaysCloseEvenWhenClickChangesActivity()
    {
        var source = Read("src/Features/Shopping/ShopperWindow.cs");
        Assert.DoesNotMatch(@"if\s*\([^\r\n]+\)\s*ImGui\.(?:BeginDisabled|EndDisabled)", source);
        Assert.Equal(1, Regex.Count(source, @"finally\s*\{\s*ImGui.EndDisabled\(\);"));
        Assert.Contains("ImGui.BeginDisabled(refreshActivity.IsActive);", source);
    }

    [Fact]
    public void PureDecisionFilesDoNotReferenceRuntimeBoundaries()
    {
        var relativePaths = new[]
        {
            "src/Features/Collection/CollectionDataOwnershipPolicy.cs",
            "src/Features/Collection/CollectionDataOwnershipSession.cs",
            "src/Features/Collection/OutfitPlanningPolicy.cs",
            "src/Features/Diagnostics/DiagnosticPolicy.cs",
            "src/Features/MarketBoard/MarketBoardContext.cs",
            "src/Features/MarketBoard/MarketBoardTabPresentation.cs",
            "src/Features/MarketBoard/MarketWorldPolicy.cs",
            "src/Features/Shopping/PurchasePolicy.cs",
            "src/Features/Shopping/ShopperPagePolicy.cs",
            "src/Features/Shopping/ShoppingProjection.cs",
            "src/Features/Collection/CollectionDataOwnershipState.cs",
            "src/Features/Collection/ItemOwnership.cs",
            "src/Shared/Operations/SingleFlightOperation.cs",
            "src/Shared/Ui/PluginMotion.cs",
        };
        var forbidden = new[]
        {
            "using Dalamud",
            "Dalamud.",
            "ImGui",
            "FFXIVClientStructs",
            "System.IO.File",
            "System.IO.Directory",
            "HttpClient",
            "DateTime.Now",
            "DateTime.UtcNow",
            "DateTimeOffset.Now",
            "DateTimeOffset.UtcNow",
        };

        foreach (var relativePath in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(Root, relativePath));
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProductionCodeHasNoHighConfidenceBlockingOrOwnershipSmells()
    {
        var patterns = new[]
        {
            @"\basync\s+void\b",
            @"\.GetAwaiter\(\)\.GetResult\(\)",
            @"\.Result\b",
            @"\.Wait\s*\(",
            @"\bThread\.Sleep\s*\(",
            @"\b(?:TODO|FIXME)\b",
            @"_\s*=\s*\w+Async\s*\(",
        };

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            Assert.All(patterns, pattern =>
                Assert.DoesNotMatch(new Regex(pattern, RegexOptions.CultureInvariant), source));
        }
    }

    [Fact]
    public void ExportaramusIsTheOnlyProductionOwnershipAuthority()
    {
        var production = string.Join('\n', Directory.EnumerateFiles(
                Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        foreach (var forbidden in new[]
        {
            "OwnedItemScanner",
            "CollectionExporter",
            "CachedWardrobe",
            "FfxivCollectCharacterId",
            "GetMissingCollectionsAsync",
            "CollectorsCompendium",
        })
        {
            Assert.DoesNotContain(forbidden, production, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            "/characters/",
            Read("src/Features/Collection/FfxivCollectClient.cs"),
            StringComparison.OrdinalIgnoreCase);

        var ownershipPolicy = Read("src/Features/Collection/CollectionDataOwnershipPolicy.cs");
        foreach (var source in new[]
        {
            "inventory",
            "armoryChest",
            "chocoboSaddlebag",
            "premiumSaddlebag",
            "retainers",
            "armoire",
            "glamourDresser",
        })
        {
            Assert.Contains($"\"{source}\"", ownershipPolicy, StringComparison.Ordinal);
        }

        var client = Read("src/Contracts/1.2/Consumer/CollectionDataIpcClient.cs");
        var adapter = Read("src/Adapters/CollectionData/ExportaramusTransport.Dalamud.cs");
        Assert.DoesNotContain("Dalamud", client, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIpcSubscriber", client, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Count(adapter, "GetIpcSubscriber", RegexOptions.CultureInvariant));
        Assert.Contains("CollectionDataGates.ReportOwnershipEvidence", adapter, StringComparison.Ordinal);
        Assert.Contains("CollectionDataGates.SnapshotWithdrawn", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionUsesInstanceServicesAndUiDoesNotDispatchNativePurchases()
    {
        var pluginSource = Read("src/Bootstrap/Plugin.cs");
        var shopperSource = Read("src/Features/Shopping/ShopperWindow.cs");
        var bookmarkSource = Read("src/Features/MarketBoard/MarketBoardBookmarkWindow.cs");

        Assert.DoesNotContain("[PluginService]", pluginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin.PluginInterface", shopperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin.PluginInterface", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIpcSubscriber", shopperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionDataClient", shopperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InfoProxyItemSearch", shopperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SendPurchaseRequestPacket", shopperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLastPurchasedItem", shopperSource, StringComparison.Ordinal);
        Assert.Equal("CollectionShopper.Decisions", typeof(PurchasePolicy).Assembly.GetName().Name);
    }

    [Fact]
    public void MarketBoardRailUsesCurrentNativeStackWithoutFocusOverrides()
    {
        var bookmarkSource = Read("src/Features/MarketBoard/MarketBoardBookmarkWindow.cs");

        Assert.Contains("AllLoadedUnitsList", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("anchor->DepthLayer", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("candidate->DepthLayer", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("DepthLayers", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PostFocusChanged", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusedAddon", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("->DrawOrderIndex", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("TryFillNativeOcclusions", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("SetNativeOcclusionUnavailable", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("PrepareNativeFrame", Read("src/Bootstrap/Plugin.cs"), StringComparison.Ordinal);
        Assert.Contains("GetWindowBounds", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("FillVisibleRegions", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("MaximumNativeOccluders = 32", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("visibleTabRegions", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("tabRegionScratch", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketBoardTabPresentation.GetVisibleRegions(", bookmarkSource, StringComparison.Ordinal);
        Assert.Contains("GetLockedCompanionPlacement", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AvoidForegroundWindows", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("searchResultsOcclusion", bookmarkSource, StringComparison.Ordinal);
        Assert.DoesNotContain("suppressedByNativeForeground", Read("src/Features/Shopping/ShopperWindow.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("CmdBuffer", Read("src/Features/Shopping/ShopperWindow.cs"), StringComparison.Ordinal);
        Assert.Contains("nativeDrawClipper.FinishFrame(nativeOcclusions)",
            Read("src/Features/Shopping/ShopperWindow.cs"), StringComparison.Ordinal);
        Assert.Contains("nativeDrawClipper.Capture(ImGui.GetWindowDrawList())",
            Read("src/Features/Shopping/ShopperWindow.cs"), StringComparison.Ordinal);
        Assert.Contains("FillVisibleRegions",
            Read("src/Shared/Ui/NativeWindowDrawClipper.Dalamud.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DrawMethodsContainNoIoScansSerializationOrTextureDiscovery()
    {
        foreach (var relativePath in new[]
        {
            "src/Features/Shopping/ShopperWindow.cs",
            "src/Features/Shopping/MarketBoardSettingsView.Dalamud.cs",
            "src/Features/Shopping/ShopperCategorySettingsView.Dalamud.cs",
            "src/Shared/Ui/PluginControls.cs",
            "src/Features/MarketBoard/MarketBoardBookmarkWindow.cs",
            "src/Features/Diagnostics/DiagnosticsWindow.cs",
        })
        {
            var source = Read(relativePath);
            foreach (var methodBody in ExtractDrawMethodBodies(source))
            {
                Assert.DoesNotContain("File.", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("Directory.", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("GetExcelSheet", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("JsonSerializer", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("JsonDocument", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("GetFromGame(", methodBody, StringComparison.Ordinal);
                Assert.DoesNotContain("GetFromGameIcon(", methodBody, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void OwnedCallbacksHaveSymmetricIdempotentTeardown()
    {
        var pluginSource = Read("src/Bootstrap/Plugin.cs");
        Assert.Contains("framework.Update += OnFrameworkUpdate", pluginSource, StringComparison.Ordinal);
        Assert.Contains("framework.Update -= OnFrameworkUpdate", pluginSource, StringComparison.Ordinal);
        Assert.Contains("UiBuilder.Draw += DrawWindows", pluginSource, StringComparison.Ordinal);
        Assert.Contains("UiBuilder.Draw -= DrawWindows", pluginSource, StringComparison.Ordinal);
        Assert.Contains("commandManager.AddHandler", pluginSource, StringComparison.Ordinal);
        Assert.Contains("commandManager.RemoveHandler", pluginSource, StringComparison.Ordinal);
        Assert.Contains("clientState.Login += OnLogin", pluginSource, StringComparison.Ordinal);
        Assert.Contains("clientState.Login -= OnLogin", pluginSource, StringComparison.Ordinal);
        Assert.Contains("clientState.Logout += OnLogout", pluginSource, StringComparison.Ordinal);
        Assert.Contains("clientState.Logout -= OnLogout", pluginSource, StringComparison.Ordinal);
        Assert.Contains("collectionDataClient.Dispose()", pluginSource, StringComparison.Ordinal);
        Assert.Contains("if (disposed)", pluginSource, StringComparison.Ordinal);

        var controllerSource = Read("src/Features/Shopping/ShoppingController.cs");
        Assert.Contains("marketBoard.OfferingsReceived += OnOfferingsReceived", controllerSource, StringComparison.Ordinal);
        Assert.Contains("marketBoard.OfferingsReceived -= OnOfferingsReceived", controllerSource, StringComparison.Ordinal);
        Assert.Contains("marketBoard.ItemPurchased += OnItemPurchased", controllerSource, StringComparison.Ordinal);
        Assert.Contains("marketBoard.ItemPurchased -= OnItemPurchased", controllerSource, StringComparison.Ordinal);
        Assert.Contains("if (disposed)", controllerSource, StringComparison.Ordinal);

        var shopperSource = Read("src/Features/Shopping/ShopperWindow.cs");
        Assert.Contains("SingleFlightOperation", shopperSource, StringComparison.Ordinal);
        Assert.Contains("refreshOperation.Dispose()", shopperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstAutomaticOwnershipSnapshotRoutesThroughTheFullCatalogRefresh()
    {
        var shopperSource = Read("src/Features/Shopping/ShopperWindow.cs");
        var route = Regex.Match(
            shopperSource,
            @"if\s*\(\s*CollectionOwnershipRefreshPolicy\.RequiresCatalogRefresh\(\s*collectionLoaded,\s*plugin\.Configuration\.AutomaticallyRefreshCollection\)\)\s*\{\s*StartRefresh\(\);\s*\}",
            RegexOptions.CultureInvariant);

        Assert.True(route.Success, "The first automatic ownership snapshot no longer starts the full catalog refresh.");
    }

    private static IEnumerable<string> ExtractDrawMethodBodies(string source)
    {
        var matches = Regex.Matches(
            source,
            @"\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:unsafe\s+)?[\w<>?]+\s+Draw\w*\s*\([^)]*\)\s*\{",
            RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            var openingBrace = source.IndexOf('{', match.Index);
            var depth = 0;
            for (var index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}' && --depth == 0)
                {
                    yield return source[openingBrace..(index + 1)];
                    break;
                }
            }
        }
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(Root, relativePath));

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
