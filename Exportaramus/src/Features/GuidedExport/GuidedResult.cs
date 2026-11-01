using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public enum GuidedResult
{
    Pending, Succeeded, Failed
}

// Store managed IDs, not native pointers.
