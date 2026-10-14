using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public static class MainIntentPolicy
{
    public static MainIntentDecision Decide(MainIntent? intent, bool guidedActive) => intent switch
    {
        MainIntent.Close => new(MainUiAction.Close, GuidedCommand.None),
        MainIntent.SyncNow => new(MainUiAction.Sync, GuidedCommand.None),
        MainIntent.ToggleReduceMotion => new(MainUiAction.ToggleReduceMotion, GuidedCommand.None),
        MainIntent.GuidedCapture => new(MainUiAction.None, guidedActive ? GuidedCommand.Cancel : GuidedCommand.Start),
        MainIntent.CancelGuided => new(MainUiAction.None, GuidedCommand.Cancel),
        MainIntent.ContinueGuided => new(MainUiAction.None, GuidedCommand.Continue),
        MainIntent.SavePartial => new(MainUiAction.None, GuidedCommand.SavePartial),
        _ => new(MainUiAction.None, GuidedCommand.None),
    };
}
