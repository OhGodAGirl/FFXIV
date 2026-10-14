namespace Exportaramus.Features.GuidedExport;

public readonly record struct GuidedValidationFacts(bool Supervised, string ValidatedVersion, string CurrentVersion,
    bool ObjectKindKnown, int DresserIdCount, int ArmoireIdCount, string DresserAddon, string ArmoireAddon,
    int UnsafeConditionCount, float InteractionRange);
