using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class DiagnosticWindow : Window, IDisposable
{
    public const float PanelWidth = SettingsWindow.PanelWidth;
    private readonly MigrationController controller;
    private readonly CollectionDataRefreshController collectionData;
    private readonly Configuration configuration;
    private readonly Action configurationChanged;
    private readonly IPluginLog log;
    private readonly DiagnosticActivityLog activity = new();
    private string report = "Waiting for the first diagnostic observation.";
    private MigrationDiagnosticSnapshot? latestSnapshot;
    private bool stylesPushed;
    private DateTime nextObservation;

    public DiagnosticWindow(
                MigrationController controller,
                DresserScanner scanner,
                NativeUi nativeUi,
                SettingsWindow settingsWindow,
                CollectionDataRefreshController collectionData,
                Configuration configuration,
                Action configurationChanged,
                IPluginLog log)
                : base(
                    "Spring Cleaning Diagnostics##SpringCleaningDiagnostics",
                    ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoResize)
    {
        this.controller = controller;
        this.collectionData = collectionData;
        this.configuration = configuration;
        this.configurationChanged = configurationChanged;
        this.log = log;
    }

    public void Dispose()
    {
    }

    public void Observe()
    {
        if (DateTime.UtcNow < nextObservation) return;
        nextObservation = DateTime.UtcNow.AddSeconds(1);
        latestSnapshot = controller.CaptureDiagnostics();
        report = controller.SupportDiagnostics.Serialize(latestSnapshot, collectionData.Snapshot,
            typeof(Plugin).Assembly.ManifestModule.ModuleVersionId);
        var observation = activity.Observe(DateTime.Now, latestSnapshot);
        if (observation.Line is null)
            return;

        if (observation.Kind == DiagnosticObservationKind.Waiting)
            log.Warning("Spring Cleaning diagnostics: {DiagnosticLine}", observation.Line);
        else
            log.Information("Spring Cleaning diagnostics: {DiagnosticLine}", observation.Line);
    }

    public void BeginStationSession()
    {
        nextObservation = DateTime.MinValue;
    }

    public override void OnClose()
    {
        configuration.DiagnosticMode = false;
        configurationChanged();
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(620f, 650f), ImGuiCond.FirstUseEver);
        SpringCleaningUi.PushMainWindowStyle();
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed) return;
        SpringCleaningUi.PopMainWindowStyle();
        stylesPushed = false;
    }

    public UiDrawObservation DrawObservation { get; private set; }

    public override void Draw()
    {
        DrawObservation = UiDrawObservation.Capture();
        if (ImGui.Button("Copy Diagnostics")) ImGui.SetClipboardText(report);
        ImGui.TextWrapped("Current observations and retained last-stop evidence. Unobserved checks are not proof of success.");
        ImGui.Separator();
        ImGui.BeginChild("Report", Vector2.Zero, false);
        ImGui.TextUnformatted(report);
        ImGui.EndChild();
    }
}
