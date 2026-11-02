using EZDelivery.Features.Interface;
using Xunit;

namespace EZDelivery.Tests.Interface;

public sealed class UiIntentRoutingTests
{
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    public void EveryIntentHasExplicitCancellationAndGenerationRules(long generationDelta)
    {
        // Arrange: expected cancellation belongs to the operation, not to context age.
        const long preparedGeneration = 17;
        var current = generationDelta == 0;
        (UiIntent Intent, bool StopDelivery, bool StopPurchases, bool ProcessCurrent)[] cases =
        [
            (new StartDeliveryIntent(preparedGeneration), false, false, current),
            (new StopDeliveryIntent(preparedGeneration), true, false, false),
            (new BuyOneIntent(preparedGeneration, "offer"), false, false, current),
            (new BuyAllIntent(preparedGeneration, ["offer"]), false, false, current),
            (new StopPurchasesIntent(preparedGeneration), false, true, current),
            (new SelectCategoryIntent(preparedGeneration, ShopCategory.Armor), false, current, current),
            (new CloseBrowserIntent(preparedGeneration), false, true, current),
            (new SearchIntent(preparedGeneration, "query"), false, false, current),
            (new ReduceMotionIntent(preparedGeneration, true), false, false, current),
            (new ShopTabStyleIntent(preparedGeneration, ShopTabStyle.RoundTabs), false, false, current),
            (new CopyDiagnosticsIntent(preparedGeneration, "report"), false, false, current),
        ];

        foreach (var (intent, stopDelivery, stopPurchases, processCurrent) in cases)
        {
            // Act
            var route = UiIntentRouting.EvaluateAfterTick(intent, preparedGeneration + generationDelta);
            var beforeTick = UiIntentRouting.EvaluateBeforeTick(intent);

            // Assert
            Assert.Equal(stopDelivery, route.StopDelivery);
            Assert.Equal(stopPurchases, route.StopPurchases);
            Assert.Equal(processCurrent, route.ProcessCurrent);
            Assert.Equal(new UiIntentRoute(stopDelivery, stopPurchases || intent is SelectCategoryIntent, false), beforeTick);
        }
    }

    [Fact]
    public void StaleCategoryStillStopsBeforeTickButCannotChangePresentationAfterTick()
    {
        // Arrange
        UiIntent[] intents = [new SelectCategoryIntent(17, ShopCategory.Weapons), new CloseBrowserIntent(17), new StopDeliveryIntent(17)];

        // Act
        var before = intents.Select(UiIntentRouting.EvaluateBeforeTick).ToArray();
        var after = intents.Select(intent => UiIntentRouting.EvaluateAfterTick(intent, 18)).ToArray();

        // Assert
        Assert.Equal(new UiIntentRoute(false, true, false), before[0]);
        Assert.Equal(new UiIntentRoute(false, false, false), after[0]);
        Assert.Equal(new UiIntentRoute(false, true, false), after[1]);
        Assert.Equal(new UiIntentRoute(true, false, false), after[2]);
    }
}
