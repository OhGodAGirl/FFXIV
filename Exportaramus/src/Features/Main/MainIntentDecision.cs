using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public readonly record struct MainIntentDecision(MainUiAction Action, GuidedCommand GuidedCommand);
