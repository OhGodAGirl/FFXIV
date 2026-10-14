using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticUi
{
    public DiagnosticVector WindowPosition { get; init; } = new(0, 0);
    public DiagnosticVector WindowSize { get; init; } = new(760, 560);
    public double ContentWidth { get; init; } = 740;
    public double UiScale { get; init; } = 1;
    public bool WideActions { get; init; } = true;
    public int CollectionColumnCount { get; init; } = 3;
    public bool FontReady { get; init; }
    public int PushedStyleVarCount { get; init; }
    public int PushedColorCount { get; init; }
    public bool ReduceMotion { get; init; }
    public long ViewStateRevision { get; init; }
}
