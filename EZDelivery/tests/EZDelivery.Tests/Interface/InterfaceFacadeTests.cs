using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class InterfaceFacadeTests
{
    private static ShopRow Row(string key, string name = "Sword", ShopCategory category = ShopCategory.Weapons,
        ulong cost = 100, bool eligible = true) => new(key, (uint)key.Aggregate(0, (value, c) => value * 31 + c), 2, name, category, cost, eligible, eligible ? "" : "Unavailable");
    private static ShoppingView Shop(params ShopRow[] rows) => new(false, true, "Collection data ready", rows, 150, "", 0);
    private static UiState State(params ShopRow[] rows) => UiState.Empty with
    { Generation = 7, LoggedIn = true, ShopAnchor = new(new(200, 100, 500, 500), true), Shopping = Shop(rows) };

    [Fact]
    public void SearchAndCategorySnapshotOnlyDisplayedEligibleOffersInOrder()
    {
        var result = InterfaceFacade.Project(Shop(Row("first", "Long SWORD"), Row("other", "Spear"),
            Row("armor", "Sword armor", ShopCategory.Armor), Row("last", "Short sword"),
            Row("blocked", "Blocked sword", eligible: false)), ShopCategory.Weapons, "sWoRd");
        Assert.Equal(new[] { "first", "last", "blocked" }, result.Rows.Select(r => r.OfferKey));
        Assert.Equal(new[] { "first", "last" }, result.BuyAllKeys);
        Assert.Equal(4, result.MissingCount);
        Assert.Equal(200UL, result.TotalCost);
    }

    [Fact]
    public void UnaffordableRowsStayInQueueSoEngineStopsWithoutSkipping()
    {
        var projection = InterfaceFacade.Project(Shop(Row("first", cost: 200), Row("later", cost: 50)), ShopCategory.Weapons, "");
        Assert.Equal(new[] { "first", "later" }, projection.BuyAllKeys);
        Assert.Equal(250UL, projection.TotalCost);
    }

    [Fact]
    public void RepeatedOfferOfSameItemCostsOnlyFirstEligibleCopy()
    {
        var first = Row("first", cost: 100);
        var duplicate = Row("second", cost: 150) with { ItemId = first.ItemId };
        var projection = InterfaceFacade.Project(Shop(first, duplicate), ShopCategory.Weapons, "");
        Assert.Equal(new[] { "first" }, projection.BuyAllKeys);
        Assert.Equal(100UL, projection.TotalCost);
        Assert.Equal(2, projection.Rows.Count);
    }

    [Fact]
    public void QueueOwnsItsSnapshotAndCannotExpandWithNewRowsOrSearchChanges()
    {
        var facade = new InterfaceFacade();
        facade.Prepare(State(Row("one")));
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons));
        var mutable = new List<string>(facade.Browser.BuyAllKeys);
        facade.Enqueue(new BuyAllIntent(7, mutable));
        mutable.Add("injected");
        facade.Prepare(State(Row("one"), Row("new")));
        facade.HandlePresentationIntent(new SearchIntent(7, "new"));
        var intent = Assert.IsType<BuyAllIntent>(Assert.Single(facade.DrainIntents()));
        Assert.Equal(new[] { "one" }, intent.OfferKeys);
    }

    [Fact]
    public void DoubleStartIsDeduplicatedButStopRemainsAvailable()
    {
        var facade = new InterfaceFacade();
        facade.Enqueue(new StartDeliveryIntent(7));
        facade.Enqueue(new StartDeliveryIntent(7));
        facade.Enqueue(new BuyOneIntent(7, "one"));
        facade.Enqueue(new StopDeliveryIntent(7));
        var pending = facade.DrainIntents();
        Assert.Collection(pending, i => Assert.IsType<StartDeliveryIntent>(i), i => Assert.IsType<StopDeliveryIntent>(i));
        facade.Enqueue(new StartDeliveryIntent(8));
        Assert.Single(facade.DrainIntents());
    }

    [Fact]
    public void ContextLossAndExplicitSameCategoryCloseResetBrowser()
    {
        var facade = new InterfaceFacade();
        facade.Prepare(State(Row("one")));
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons));
        Assert.Equal(ShopCategory.Weapons, facade.SelectedCategory);
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons));
        Assert.Null(facade.SelectedCategory);
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Armor));
        facade.Prepare(State() with { LoggedIn = false });
        Assert.Null(facade.SelectedCategory);
        Assert.Empty(facade.Browser.Rows);
    }

    [Fact]
    public void StalePresentationIntentsDoNotChangeNewContext()
    {
        var facade = new InterfaceFacade();
        facade.Prepare(State());
        Assert.False(facade.HandlePresentationIntent(new SelectCategoryIntent(6, ShopCategory.Weapons)));
        Assert.Null(facade.SelectedCategory);
    }

    [Fact]
    public void UnknownCollectionCannotDisplayPreparedStaleMissingRows()
    {
        var projection = InterfaceFacade.Project(Shop(Row("stale")) with { CollectionUsable = false }, ShopCategory.Weapons, "");
        Assert.Empty(projection.Rows);
        Assert.Empty(projection.BuyAllKeys);
    }

    [Fact]
    public void CheckedSealTotalNeverWrapsIntoAffordableValue()
    {
        var projection = InterfaceFacade.Project(Shop(Row("one", cost: ulong.MaxValue), Row("two", cost: 1)), ShopCategory.Weapons, "");
        Assert.Null(projection.TotalCost);
    }

    [Fact]
    public void StopSurvivesSaturatedInputQueue()
    {
        var facade = new InterfaceFacade();
        for (var i = 0; i < 70; i++) facade.Enqueue(new SearchIntent(7, i.ToString()));
        facade.Enqueue(new StopPurchasesIntent(7));
        Assert.IsType<StopPurchasesIntent>(Assert.Single(facade.DrainIntents()));
    }

    [Fact]
    public void CategoryLimitAppliesBeforeSearchWithoutCountingOtherCategories()
    {
        var rows = Enumerable.Range(0, 2050).SelectMany(i => new[]
        {
            Row($"armor{i}", "needle", ShopCategory.Armor),
            Row($"weapon{i}", i == 2047 || i == 2048 ? "needle" : "other"),
        }).ToArray();
        var result = InterfaceFacade.Project(Shop(rows), ShopCategory.Weapons, " needle ");
        Assert.Equal(2048, result.MissingCount);
        Assert.Equal("weapon2047", Assert.Single(result.Rows).OfferKey);
        Assert.Equal(new[] { "weapon2047" }, result.BuyAllKeys);
        Assert.True(result.SearchActive);
    }

    [Fact]
    public void OverflowKeepsLaterKeysAndDisabledDuplicateDoesNotClaimItem()
    {
        var disabled = Row("disabled", eligible: false);
        var first = Row("first", cost: ulong.MaxValue) with { ItemId = disabled.ItemId };
        var result = InterfaceFacade.Project(Shop(disabled, first, Row("overflow", cost: 1), Row("later")), ShopCategory.Weapons, "");
        Assert.Equal(new[] { "first", "overflow", "later" }, result.BuyAllKeys);
        Assert.Null(result.TotalCost);
        Assert.Equal(4, result.Rows.Count);
    }

    [Fact]
    public void GeneratedProjectionsPreserveVisibleOrderUniqueEligibleItemsAndExactSum()
    {
        var random = new Random(71032);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var rows = Enumerable.Range(0, random.Next(1, 150)).Select(i =>
                new ShopRow($"offer{i}", (uint)random.Next(1, 20), 2,
                    random.Next(2) == 0 ? "Steel sword" : "Bronze spear",
                    (ShopCategory)random.Next(4), (ulong)random.Next(0, 20000), random.Next(3) != 0, "")).ToArray();
            var category = (ShopCategory)random.Next(4);
            var search = iteration % 2 == 0 ? " SWORD " : "";
            var categoryRows = rows.Where(row => row.Category == category).ToArray();
            var expectedRows = categoryRows.Where(row => row.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            var expectedQueue = expectedRows.Where(row => row.CanBuy).DistinctBy(row => row.ItemId).ToArray();
            var result = InterfaceFacade.Project(Shop(rows), category, search);
            Assert.Equal(expectedRows, result.Rows);
            Assert.Equal(expectedQueue.Select(row => row.OfferKey), result.BuyAllKeys);
            Assert.Equal(expectedQueue.Aggregate(0UL, (sum, row) => sum + row.SealCost), result.TotalCost);
            Assert.Equal(categoryRows.Length, result.MissingCount);
        }
    }

    [Fact]
    public void PresentationLifetimeRejectsOldGenerationAndReleasesOldCharacterRows()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var facade = new InterfaceFacade();
            facade.Prepare(State(Row("old")));
            Assert.True(facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons)));
            Assert.True(facade.HandlePresentationIntent(new SearchIntent(7, new string('x', 200))));
            Assert.Equal(160, facade.Search.Length);
            facade.Prepare(State() with { Generation = 8, LoggedIn = false });
            Assert.Empty(facade.Search);
            Assert.Empty(facade.Browser.Rows);
            Assert.Null(facade.SelectedCategory);
            Assert.False(facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons)));
            Assert.Empty(facade.DrainIntents());
            Assert.Empty(facade.DrainIntents());
        }
    }

    [Fact]
    public void InputQueueAndPurchaseSnapshotHaveIndependentBounds()
    {
        var facade = new InterfaceFacade();
        facade.Enqueue(new BuyAllIntent(7, Enumerable.Range(0, 2050).Select(i => i.ToString()).ToArray()));
        var purchase = Assert.IsType<BuyAllIntent>(Assert.Single(facade.DrainIntents()));
        Assert.Equal(2048, purchase.OfferKeys.Count);
        Assert.Equal("2047", purchase.OfferKeys[^1]);
        for (var i = 0; i < 70; i++) facade.Enqueue(new SearchIntent(7, i.ToString()));
        Assert.Equal(64, facade.DrainIntents().Count);
        for (var i = 0; i < 64; i++) facade.Enqueue(new SearchIntent(7, i.ToString()));
        facade.Enqueue(new CloseBrowserIntent(7));
        Assert.IsType<CloseBrowserIntent>(Assert.Single(facade.DrainIntents()));
    }

    [Fact]
    public void SettingsClipboardAndTransactionIntentsAreNotPresentationMutations()
    {
        var facade = new InterfaceFacade();
        facade.Prepare(State(Row("one")));
        var intents = new UiIntent[] { new ReduceMotionIntent(7, true), new CopyDiagnosticsIntent(7, "prepared report"),
            new BuyOneIntent(7, "one"), new StartDeliveryIntent(7), new StopDeliveryIntent(7), new StopPurchasesIntent(7) };
        foreach (var intent in intents) Assert.False(facade.HandlePresentationIntent(intent));
        facade.Enqueue(intents[0]);
        facade.Enqueue(intents[1]);
        Assert.Collection(facade.DrainIntents(),
            intent => Assert.True(Assert.IsType<ReduceMotionIntent>(intent).Enabled),
            intent => Assert.Equal("prepared report", Assert.IsType<CopyDiagnosticsIntent>(intent).PreparedReport));
        Assert.Null(facade.SelectedCategory);
    }

    [Fact]
    public void ExplicitCloseClearsCategoryWhileMissingAnchorAlsoClearsSearch()
    {
        var facade = new InterfaceFacade();
        facade.Prepare(State(Row("one")));
        facade.SettingsOpen = true;
        facade.DiagnosticsOpen = true;
        Assert.True(facade.SettingsOpen);
        Assert.True(facade.DiagnosticsOpen);
        facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons));
        facade.HandlePresentationIntent(new SearchIntent(7, "Sword"));
        Assert.True(facade.HandlePresentationIntent(new CloseBrowserIntent(7)));
        Assert.Null(facade.SelectedCategory);
        Assert.Empty(facade.Browser.Rows);
        facade.Prepare(State() with { ShopAnchor = null });
        Assert.Empty(facade.Search);
        Assert.False(facade.HandlePresentationIntent(new SelectCategoryIntent(7, ShopCategory.Weapons)));
    }
}
