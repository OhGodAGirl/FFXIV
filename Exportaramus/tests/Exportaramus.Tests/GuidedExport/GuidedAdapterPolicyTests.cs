using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Tests.GuidedExport;

public sealed class GuidedAdapterPolicyTests
{
    private static GuidedValidationFacts Valid => new(true, "build", "build", true, 1, 1, "Dresser", "Armoire", 1, 3);
    [Fact]
    public void DevelopmentCompatibilityDoesNotClaimSupervisedValidation()
    {
        var compatible = Valid with { Supervised = false };
        Assert.True(GuidedAdapterPolicy.IsCompatible(compatible));
        Assert.False(GuidedAdapterPolicy.IsValidated(compatible));
        Assert.False(GuidedAdapterPolicy.IsCompatible(compatible with { CurrentVersion = "changed" }));
        Assert.False(GuidedAdapterPolicy.IsCompatible(compatible with { ArmoireIdCount = 0 }));
    }
    [Fact]
    public void EveryValidationRecordRequirementMustHoldBeforeNativeObservationOrInput()
    {
        Assert.True(GuidedAdapterPolicy.IsValidated(Valid));
        var invalid = new[] { Valid with { Supervised = false }, Valid with { ValidatedVersion = "" }, Valid with { ValidatedVersion = " " },
            Valid with { CurrentVersion = "other" }, Valid with { ObjectKindKnown = false }, Valid with { DresserIdCount = 0 },
            Valid with { ArmoireIdCount = 0 }, Valid with { DresserAddon = " " }, Valid with { ArmoireAddon = "" },
            Valid with { UnsafeConditionCount = 0 }, Valid with { InteractionRange = float.NaN }, Valid with { InteractionRange = float.PositiveInfinity },
            Valid with { InteractionRange = 0 }, Valid with { InteractionRange = -1 } };
        Assert.All(invalid, value => Assert.False(GuidedAdapterPolicy.IsValidated(value)));
    }
    [Theory]
    [InlineData(GuidedEffectKind.SelectTarget)]
    [InlineData(GuidedEffectKind.Interact)]
    [InlineData(GuidedEffectKind.SelectArmoireMenu)]
    [InlineData(GuidedEffectKind.Capture)]
    [InlineData(GuidedEffectKind.CloseAddon)]
    [InlineData(GuidedEffectKind.SaveFull)]
    [InlineData(GuidedEffectKind.SavePartial)]
    public void OnlyKnownEffectsWithinTheActiveIdentityGenerationAndThreadAreAuthorized(GuidedEffectKind kind)
    {
        Assert.True(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, true, true, 123, 123));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(false, 4, 4, kind, true, true, 123, 123));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 3, 4, kind, true, true, 123, 123));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, false, true, 123, 123));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, true, false, 123, 123));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, true, true, 0, 0));
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, true, true, 124, 123));
    }
    [Theory]
    [InlineData(GuidedEffectKind.None)]
    [InlineData((GuidedEffectKind)999)]
    public void UnknownEffectsCannotFallThroughToInteraction(GuidedEffectKind kind) =>
        Assert.False(GuidedAdapterPolicy.AuthorizeEffect(true, 4, 4, kind, true, true, 123, 123));
}
