using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticPayloadBytes(int Capabilities, int Snapshot, int PendingEvent);
