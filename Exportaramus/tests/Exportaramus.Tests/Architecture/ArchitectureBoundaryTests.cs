using System.Reflection;
using System.Text.RegularExpressions;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Architecture;

public sealed class ArchitectureBoundaryTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository source is required for architecture checks.");
        }
    }
    private static IEnumerable<string> Sources => Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories);

    [Fact]
    public void PureAssemblyHasNoHostGameOrNetworkAssemblyDependency()
    {
        var references = typeof(MainPresenter).Assembly.GetReferencedAssemblies().Select(x => x.Name!);
        Assert.DoesNotContain(references, name => name.StartsWith("Dalamud", StringComparison.Ordinal) || name.StartsWith("FFXIVClientStructs", StringComparison.Ordinal) || name.StartsWith("Lumina", StringComparison.Ordinal) || name is "System.Net.Http" or "System.Net.Sockets");
        var pureSources = Sources.Where(path => !path.EndsWith(".Dalamud.cs", StringComparison.Ordinal));
        foreach (var path in pureSources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(@"(?m)^using\s+(Dalamud|FFXIVClientStructs|Lumina)\b", source);
            Assert.DoesNotMatch(@"\b(AtkUnitBase|InventoryManager|TargetSystem)\s*\*", source);
        }
    }

    [Fact]
    public void ProductionDoesNotCreateNetworkClientsOrInspectAnotherPlugin()
    {
        foreach (var path in Sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(@"\b(HttpClient|WebClient|HttpWebRequest|TcpClient|UdpClient|ClientWebSocket)\b", source);
            Assert.DoesNotMatch(@"\b(Assembly\.LoadFrom|Assembly\.LoadFile|GetInstalledPlugins|InstalledPlugins|GetPluginInstance)\b", source);
            Assert.DoesNotContain("Process.Start", source);
        }
    }

    [Fact]
    public void RenderersUsePreparedViewsAndIntentDelegatesWithoutDomainWork()
    {
        foreach (var path in Sources.Where(path => path.EndsWith(".Dalamud.cs", StringComparison.Ordinal) &&
            (path.Contains(Path.Combine("Features", "Main"), StringComparison.Ordinal) || path.EndsWith("DiagnosticsWindow.Dalamud.cs", StringComparison.Ordinal))))
        {
            var source = File.ReadAllText(path);
            Assert.Contains("ViewState", source);
            Assert.DoesNotMatch(@"\b(JsonSerializer|JsonDocument|File|Directory|Task|Thread|SnapshotController|CollectionReader|CollectionDataAssembler|DateTimeOffset|Stopwatch)\b", source);
            Assert.DoesNotContain(".Update(", source);
            Assert.DoesNotContain("SetClipboardText", source);
            Assert.DoesNotContain("$\"", source);
        }
        var diagnostic = File.ReadAllText(Path.Combine(Root, "src/Features/Diagnostics/DiagnosticsWindow.Dalamud.cs"));
        Assert.Contains("report.Sections", diagnostic);
        Assert.DoesNotContain("report.Json", diagnostic);
    }

    [Fact]
    public void WindowStyleIsAnUnboxedValueOwnedAndDisposedByEachWindow()
    {
        var style = File.ReadAllText(Path.Combine(Root, "src/Shared/Ui/WindowStyle.Dalamud.cs"));
        Assert.Contains("internal struct WindowStyle : IDisposable", style);
        Assert.Contains("catch { Dispose(); throw; }", style);
        foreach (var path in new[] { "src/Features/Main/MainWindow.Dalamud.cs", "src/Features/Diagnostics/DiagnosticsWindow.Dalamud.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root, path));
            Assert.Contains("private WindowStyle style;", source);
            Assert.Contains("PostDraw() => style.Dispose();", source);
        }
        Assert.DoesNotContain("$\"", File.ReadAllText(Path.Combine(Root, "src/Shared/Ui/Drawing.Dalamud.cs")));
    }

    [Fact]
    public void LiveInputSurfaceIsConfinedToTheGuidedAdapterAndNeverCalledFromTests()
    {
        foreach (var path in Sources.Where(path => !path.EndsWith("GuidedCaptureAdapter.Dalamud.cs", StringComparison.Ordinal)
            && !path.EndsWith("RetainerGuideAdapter.Dalamud.cs", StringComparison.Ordinal)))
            Assert.DoesNotMatch(@"\b(InteractWithObject|SetHardTarget|DispatchItemEvent)\s*\(|->Close\s*\(", File.ReadAllText(path));
        var retainerAdapter = File.ReadAllText(Path.Combine(Root, "src/Features/Inventory/RetainerGuideAdapter.Dalamud.cs"));
        Assert.Contains("framework.IsInFrameworkUpdateThread", retainerAdapter);
        Assert.Contains("player.ContentId == contentId", retainerAdapter);
        Assert.Contains("Version == ReaderCompatibility.CurrentGameVersion", retainerAdapter);
        Assert.Contains("capturedAddon", retainerAdapter);
        Assert.Single(Regex.Matches(retainerAdapter, @"->InteractWithObject\("));
        // Retainer exit uses the validated owner callback; only final window cleanup uses Close.
        Assert.Single(Regex.Matches(retainerAdapter, @"->Close\("));
        Assert.Single(Regex.Matches(retainerAdapter, @"->FireCallbackInt\(-1\)"));
        Assert.Contains("callback.AgentInterface != (AgentInterface*)retainer", retainerAdapter);
        Assert.Contains("callback.EventKind != 0", retainerAdapter);
        Assert.Contains("InventoryVisible || !RetainerContextCleared()", retainerAdapter);
        var adapter = File.ReadAllText(Path.Combine(Root, "src/Features/GuidedExport/GuidedCaptureAdapter.Dalamud.cs"));
        Assert.Contains("SupervisedValidationComplete", adapter);
        Assert.Contains("framework.IsInFrameworkUpdateThread", adapter);
        Assert.Contains("GuidedAdapterPolicy.AuthorizeEffect", adapter);
        Assert.Contains("player.ContentId, expectedContentId", adapter);
        Assert.Contains("Take(1024)", adapter);
        Assert.Single(Regex.Matches(adapter, @"->InteractWithObject\("));
        Assert.Single(Regex.Matches(adapter, @"->Close\("));
        var project = File.ReadAllText(Path.Combine(Root, "tests/Exportaramus.Testable/Exportaramus.Testable.csproj"));
        Assert.Contains("Exclude=\"../../src/**/*.Dalamud.cs\"", project);
    }
}
