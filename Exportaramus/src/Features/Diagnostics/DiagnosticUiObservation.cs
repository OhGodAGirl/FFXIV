namespace Exportaramus.Features.Diagnostics;

public readonly record struct DiagnosticUiObservation(double X, double Y, double Width, double Height,
    float ContentWidth, double Scale, bool FontReady, int StyleVariables, int Colors, bool ReduceMotion, long Revision);
