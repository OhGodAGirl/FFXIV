using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>Orders command mutation, preview and diagnostic effects on the framework thread.</summary>
internal sealed class CommandExecutionCoordinator(
    Func<PreviewContext> context,
    Action<MainCommand> execute,
    PreviewCoordinator preview,
    DiagnosticJournal diagnostics,
    Func<DateTimeOffset> clock,
    Action<MainCommand> reportFailure,
    Action refresh)
{
    internal void Execute(MainCommand command)
    {
        try
        {
            diagnostics.Begin(DiagnosticPhase.LocalOperation, clock());
            if (command is MainCommand.LoadActive or MainCommand.ClearOutfitItems)
                preview.Cancel();
            var previousSelection = context().View.SelectedPlateId;
            execute(command);
            if (CommandPreviewPolicy.ShouldPreview(command, context().EditorOpen, context().DyeWindowOpen,
                    previousSelection, context().View.SelectedPlateId))
                preview.Request(CommandPreviewPolicy.ReportSuccess(command));
            if (diagnostics.Current.Stop == DiagnosticStop.None && !preview.IsPending)
                diagnostics.Finish(DiagnosticStop.Completed, clock());
        }
        catch
        {
            reportFailure(command);
            throw;
        }
        if (command is MainCommand.RefreshCollection)
        {
            refresh();
        }
    }

}
