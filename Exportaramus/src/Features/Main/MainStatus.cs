using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public enum MainStatus { NoCharacter, NoSnapshot, Synchronizing, Current, Partial, Error }
