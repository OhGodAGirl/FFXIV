using EZDelivery.Bootstrap;
using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class PresentationContractTests
{
    [Fact]
    public void UnpreparedPresentationIsUnavailableAndCannotImplyRunningOrMissingItems()
    {
        var facade = new InterfaceFacade();
        Assert.Empty(facade.Search);
        Assert.False(facade.Browser.SearchActive);
        Assert.Empty(facade.Browser.Rows);
        Assert.Empty(facade.Browser.BuyAllKeys);
        Assert.Equal(0UL, facade.Browser.TotalCost);
        Assert.Equal(0, facade.Browser.MissingCount);
        var state = facade.State;
        Assert.Equal(UiState.Empty, state);
        Assert.False(state.LoggedIn);
        Assert.False(state.ExpertDeliverySelected);
        Assert.False(state.Delivery.Running);
        Assert.False(state.Delivery.CanStart);
        Assert.Equal("Open the Personnel Officer's Expert Delivery tab.", state.Delivery.Status);
        Assert.False(state.Shopping.Running);
        Assert.False(state.Shopping.CollectionUsable);
        Assert.Equal("Collection data unavailable", state.Shopping.CollectionStatus);
        Assert.Empty(state.Shopping.Status);
        Assert.False(state.AnyOperationRunning);
        Assert.False(state.ReduceMotion);
        Assert.Equal("No diagnostics prepared.", state.DiagnosticsReport);
    }

    [Fact]
    public void UnreadyShopResetsSelectedCategoryAndSearchEvenWhileLoggedIn()
    {
        var facade = new InterfaceFacade();
        var state = UiState.Empty with { Generation = 1, LoggedIn = true, ShopAnchor = new(new(100, 100, 300, 300), true) };
        facade.Prepare(state);
        facade.HandlePresentationIntent(new SelectCategoryIntent(1, ShopCategory.Weapons));
        facade.HandlePresentationIntent(new SearchIntent(1, "sword"));
        facade.Prepare(state with { ShopAnchor = new(new(100, 100, 300, 300), false) });
        Assert.Null(facade.SelectedCategory);
        Assert.Empty(facade.Search);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("sword", true)]
    public void EmptyUnavailableAndPopulatedProjectionsRetainActualSearchState(string search, bool active)
    {
        var row = new ShopRow("key", 1, 1, "sword", ShopCategory.Weapons, 1, true, "");
        var shop = new ShoppingView(false, true, "Ready", [row], 1, "", 0);
        Assert.Equal(active, InterfaceFacade.Project(shop, null, search).SearchActive);
        Assert.Equal(active, InterfaceFacade.Project(shop with { Rows = [] }, ShopCategory.Weapons, search).SearchActive);
        Assert.Equal(active, InterfaceFacade.Project(shop with { CollectionUsable = false }, ShopCategory.Weapons, search).SearchActive);
        Assert.Equal(active, InterfaceFacade.Project(shop, ShopCategory.Weapons, search).SearchActive);
    }

    [Fact]
    public void ExactMaximumTotalIsRepresentableAndDoesNotBecomeOverflow()
    {
        var row = new ShopRow("key", 1, 1, "sword", ShopCategory.Weapons, ulong.MaxValue, true, "");
        var shop = new ShoppingView(false, true, "Ready", [row], 1, "", 0);
        Assert.Equal(ulong.MaxValue, InterfaceFacade.Project(shop, ShopCategory.Weapons, "").TotalCost);
    }

    [Fact]
    public void MissingDiagnosticObservationsAreExplicitAndReportRemainsReadable()
    {
        var report = new DiagnosticReport("1.0", DateTimeOffset.UnixEpoch, "0.1.0.0", false, "Unreviewed", "Idle", "None", 0,
            "Ready", 0, "Unavailable", null, "None", [], 0, 0, 0);
        Assert.Equal("Not observed", report.DeliveryDialogCheck);
        Assert.Equal("Unknown", report.LoadedBuildId);
        Assert.Equal("Not observed", report.ShoppingDialogCheck);
        Assert.Equal("Not observed", report.DeliveryMappingCheck);
        Assert.Equal("Not observed", report.DeliveryRefreshCheck);
        Assert.Contains("\n  \"SchemaVersion\"", report.ToJson());
    }

    [Fact]
    public void CleanupFailureRetainsUsefulOperationLabel()
    {
        var error = Assert.Throws<AggregateException>(() => CleanupActions.Run(() => throw new InvalidOperationException("release")));
        Assert.StartsWith("EZ Delivery cleanup failed.", error.Message);
    }

    [Theory]
    [InlineData(float.NegativeInfinity, 0, 1, 1)]
    [InlineData(0, float.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, float.PositiveInfinity, 1)]
    [InlineData(0, 0, 1, float.PositiveInfinity)]
    [InlineData(0, 0, 1, 0)]
    public void EveryNonfiniteDimensionAndZeroHeightRejectsGeometry(float x, float y, float width, float height)
        => Assert.False(new UiRect(x, y, width, height).IsValid);

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(10, 0)]
    [InlineData(0, -10)]
    [InlineData(0, 10)]
    public void EdgeTouchingDoesNotCoverNativeActionArea(float x, float y)
        => Assert.False(new UiRect(0, 0, 10, 10).Intersects(new(x, y, 10, 10)));
}
