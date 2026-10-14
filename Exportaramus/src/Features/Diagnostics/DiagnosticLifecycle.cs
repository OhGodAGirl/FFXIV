using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticLifecycle
{
    public string State { get; init; } = "Constructed";
    public Guid? ProviderSessionId { get; init; }
    public long Revision { get; init; }
    public DiagnosticRegistrations Registrations { get; init; } = new();
    public ImmutableArray<DiagnosticTask> OwnedTasks { get; init; } = [];
    public bool CancellationRequested { get; init; }
}
