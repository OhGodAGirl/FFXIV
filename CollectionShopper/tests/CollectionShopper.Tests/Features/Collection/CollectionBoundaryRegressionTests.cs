using System.Net;
using System.Text;
using System.Text.Json;

namespace CollectionShopper.Tests;

public sealed class CollectionBoundaryRegressionTests
{
    [Fact]
    public async Task NullCatalogResponseFailsAndDoesNotBecomeAnEmptyCollection()
    {
        using var client = new FfxivCollectClient(new HttpClient(new NullCatalogHandler()));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetCollectionsAsync(CancellationToken.None));
        Assert.Contains("no usable data", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBundledResourceIsAnExplicitFailure()
    {
        var error = Assert.Throws<InvalidDataException>(() => OutfitCatalog.LoadResource(null));
        Assert.Contains("CollectionShopper.Data.outfits.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HqOnlyListingAndMissingPriceObjectsRemainDistinct()
    {
        using var document = JsonDocument.Parse("""{"listings":[{"pricePerUnit":25,"quantity":1,"hq":true}]}""");
        var prices = new Dictionary<uint, MarketPriceQuote>();
        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);
        var quote = Assert.Single(prices).Value;
        Assert.Null(quote.NormalQuality);
        Assert.Equal(new MarketPrice(42, 25, 1, "", true), quote.HighQuality);
        var item = new ShoppingItem(42, "Item", 1, "Collection", null, "", "", 0, true, false, null, ShoppingCategory.Outfit, 0, 1);
        Assert.Null(ShoppingProjection.PreferredPrice(item, true));
        Assert.Null(ShoppingProjection.PreferredPrice(item with { Prices = new(null, null) }, true));
        Assert.Equal(quote.HighQuality, ShoppingProjection.PreferredPrice(item with { Prices = quote }, true));
        Assert.Null(ShoppingProjection.PreferredPrice(item with { Prices = quote }, false));
    }

    [Fact]
    public void EmptyListingArrayDoesNotReplaceAnExistingQuote()
    {
        using var document = JsonDocument.Parse("{\"listings\":[]}");
        var original = new MarketPriceQuote(new(42, 10, 1, "World", false), null);
        var prices = new Dictionary<uint, MarketPriceQuote> { [42] = original };
        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);
        Assert.Same(original, prices[42]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"minListing\":null}")]
    [InlineData("{\"minListing\":{}}")]
    [InlineData("{\"minListing\":{\"world\":null}}")]
    [InlineData("{\"minListing\":{\"world\":{\"price\":0}}}")]
    public void MalformedAggregatedQualityNeverProducesAnEstimate(string quality)
    {
        using var document = JsonDocument.Parse("{\"itemId\":42,\"nq\":" + quality + ",\"hq\":" + quality + "}");
        var prices = new Dictionary<uint, MarketPriceQuote>();
        MarketPriceResponsePolicy.AddAggregatedMinimums(prices, "World", document.RootElement);
        Assert.Empty(prices);
    }

    private sealed class NullCatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json"),
            });
    }
}
