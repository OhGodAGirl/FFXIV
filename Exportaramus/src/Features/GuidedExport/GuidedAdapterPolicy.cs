namespace Exportaramus.Features.GuidedExport;

public static class GuidedAdapterPolicy
{
    public static bool IsValidated(GuidedValidationFacts value) => value.Supervised && IsCompatible(value);
    public static bool IsCompatible(GuidedValidationFacts value) =>
        !string.IsNullOrWhiteSpace(value.ValidatedVersion) && value.ValidatedVersion == value.CurrentVersion && value.ObjectKindKnown &&
        value.DresserIdCount > 0 && value.ArmoireIdCount > 0 && !string.IsNullOrWhiteSpace(value.DresserAddon) &&
        !string.IsNullOrWhiteSpace(value.ArmoireAddon) && value.UnsafeConditionCount > 0 &&
        float.IsFinite(value.InteractionRange) && value.InteractionRange > 0;

    public static bool AuthorizeEffect(bool ownsWindowEdges, long effectGeneration, long currentGeneration,
        GuidedEffectKind kind, bool frameworkThread, bool playerLoaded, ulong actualContentId, ulong expectedContentId) =>
        ownsWindowEdges && effectGeneration == currentGeneration &&
        (kind == GuidedEffectKind.SelectTarget || kind == GuidedEffectKind.Interact || kind == GuidedEffectKind.Capture ||
        kind == GuidedEffectKind.CloseAddon || kind == GuidedEffectKind.SelectArmoireMenu || kind == GuidedEffectKind.SaveFull || kind == GuidedEffectKind.SavePartial) &&
        frameworkThread && playerLoaded && expectedContentId != 0 && actualContentId == expectedContentId;
}
