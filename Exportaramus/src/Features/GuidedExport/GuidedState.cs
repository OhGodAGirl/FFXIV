namespace Exportaramus.Features.GuidedExport;

public enum GuidedState
{
    Idle, NeedDresser, RequestDresserInteraction, WaitForDresserReady, CaptureDresser,
    RequestDresserClose, WaitForDresserClosed, NeedArmoire, RequestArmoireInteraction,
    WaitForArmoireReady, CaptureArmoire, RequestArmoireClose, WaitForArmoireClosed,
    SaveFullSnapshot, SavePartialSnapshot, Completed, Paused, PausedWithPartial, Cancelled, Failed,
}
