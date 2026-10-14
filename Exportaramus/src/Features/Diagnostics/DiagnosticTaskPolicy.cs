namespace Exportaramus.Features.Diagnostics;

public static class DiagnosticTaskPolicy
{
    public static string State(TaskStatus? status) => status switch
    {
        null or TaskStatus.Created => "NotStarted",
        TaskStatus.Running => "Running",
        TaskStatus.RanToCompletion => "Completed",
        TaskStatus.Canceled => "Cancelled",
        TaskStatus.Faulted => "Faulted",
        _ => "Waiting",
    };
}
