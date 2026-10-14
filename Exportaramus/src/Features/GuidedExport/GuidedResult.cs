using System.Collections.Immutable;

namespace Exportaramus.Features.GuidedExport;

public enum GuidedResult { Pending, Succeeded, Failed }

// Identities are stable managed tokens. Neither this model nor the controller retains native pointers.
