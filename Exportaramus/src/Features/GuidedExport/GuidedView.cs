namespace Exportaramus.Features.GuidedExport;

public sealed record GuidedView(GuidedState State, string StepLabel, string Prompt,
    bool Active, bool Paused, bool CanSavePartial, bool DresserCompleted, bool ArmoireCompleted);
