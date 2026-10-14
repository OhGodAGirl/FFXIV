using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticRegistrations(bool FrameworkUpdate = false, bool ConditionChange = false, bool Command = false,
    bool Draw = false, bool OpenMainUi = false, bool OpenConfigUi = false, bool IpcGates = false, bool Windows = false);
