using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SpringCleaning;

internal sealed class SettingsWindow : Window, IDisposable
{
    public const float PanelWidth = 360f;
    public const float PanelHeight = 390f;
    private readonly Configuration configuration;
    private readonly Action configurationChanged;
    private Vector2? dockedPosition;
    private Vector2? dockedSize;
    private bool placementApplied;
    private bool stylesPushed;

    public SettingsWindow(
                Configuration configuration,
                Action configurationChanged)
                : base(
                    "Settings##SpringCleaningSettings",
                    ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoResize)
    {
        this.configuration = configuration;
        this.configurationChanged = configurationChanged;
        IsOpen = false;
        Size = new Vector2(PanelWidth, PanelHeight);
        SizeCondition = ImGuiCond.Always;
    }

    public float? ScreenLeft { get; private set; }

    public float? ScreenRight { get; private set; }

    public float? ScreenBottom { get; private set; }

    public void Dispose()
    {
    }

    public void MatchDresser(Vector2 position)
    {
        if (placementApplied)
            return;

        dockedPosition = position;
        placementApplied = true;
    }

    public void MatchArmoire(Vector2 position, Vector2 size)
    {
        // Match native screen dimensions without applying ImGui scaling a second time.
        dockedSize = size;
        dockedPosition = position;
    }

    public void BeginStationSession(bool snapToDefault)
    {
        dockedPosition = null;
        dockedSize = null;
        placementApplied = !snapToDefault;
        ScreenLeft = null;
        ScreenRight = null;
        ScreenBottom = null;
    }

    public override void PreDraw()
    {
        if (dockedPosition is { } position)
        {
            Position = null;
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        }
        Size = null;
        ImGui.SetNextWindowSize(dockedSize ?? new Vector2(PanelWidth, PanelHeight), ImGuiCond.Always);

        SpringCleaningUi.PushMainWindowStyle();
        stylesPushed = true;
    }

    public override void PostDraw()
    {
        if (!stylesPushed)
            return;

        SpringCleaningUi.PopMainWindowStyle();
        stylesPushed = false;
    }

    public UiDrawObservation DrawObservation { get; private set; }

    public override void Draw()
    {
        DrawObservation = UiDrawObservation.Capture();
        var windowPosition = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        ScreenLeft = windowPosition.X;
        ScreenRight = windowPosition.X + windowSize.X;
        ScreenBottom = windowPosition.Y + windowSize.Y;

        SettingsPanel.Draw(configuration, configurationChanged);
    }
}
