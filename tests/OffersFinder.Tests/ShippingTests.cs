using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class ShippingTests
{
    [Theory]
    [InlineData("Envío gratis", 0.0)]
    [InlineData("Free delivery", 0.0)]
    [InlineData("Envío de 4,99 €", 4.99)]
    [InlineData("+ 6,90 € de envío", 6.90)]
    [InlineData("Envío en 2 días", null)]
    [InlineData("Recogida en tienda", null)]
    [InlineData(null, null)]
    public void Parses_shipping_text(string? text, double? expected)
    {
        Assert.Equal(expected is null ? null : (decimal)expected.Value, Offer.ParseShipping(text));
    }

    [Fact]
    public void Shopping_offers_carry_shipping_and_sort_by_effective_price()
    {
        const string json = """
            {"shopping":[
              {"title":"Molinillo X","source":"A","link":"https://a/x","price":"79,00 €","delivery":"Envío de 9,99 €"},
              {"title":"Molinillo X","source":"B","link":"https://b/x","price":"84,00 €","delivery":"Envío gratis"},
              {"title":"Molinillo X","source":"C","link":"https://c/x","price":"82,00 €"}
            ]}
            """;
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, new List<Store>());
        var results = ShoppingSearchClient.Parse(json);

        var byPrice = searcher.OffersFromShopping(new WatchItem { Query = "Molinillo X" }, results);
        Assert.Equal(9.99m, byPrice.Single(o => o.StoreName == "A").ShippingCost);
        Assert.Equal("envío gratis", byPrice.Single(o => o.StoreName == "B").ShippingLabel);
        Assert.Equal("envío no indicado", byPrice.Single(o => o.StoreName == "C").ShippingLabel);

        var item = new WatchItem { Query = "Molinillo X", CompareWithShipping = true };
        var sorted = searcher.OffersFromShopping(item, results).OrderBy(o => o.EffectivePrice(item)).Select(o => o.StoreName).ToList();
        Assert.Equal(new[] { "C", "B", "A" }, sorted);   // 82 (sin dato) < 84 (gratis) < 88,99 (79 + 9,99)
    }

    [Fact]
    public void Summary_and_report_show_shipping()
    {
        var now = DateTimeOffset.UtcNow;
        var watchlist = new Watchlist { Items = { new WatchItem { Name = "Molinillo", Query = "m" } } };
        var history = new Dictionary<string, ItemHistory>
        {
            ["query:m"] = new ItemHistory
            {
                Records = { new PriceRecord { Timestamp = now, Price = 79, Store = "A" } },
                StoreRecords = new() { new StoreRecord { Timestamp = now, Store = "A", Price = 79, ShippingCost = 9.99m }, new StoreRecord { Timestamp = now, Store = "B", Price = 84, ShippingCost = 0 } },
                LastOffersAt = now,
                LastOffers = new() { new OfferRecord { Store = "A", Price = 79, Title = "Molinillo", Url = "https://a/x", ShippingCost = 9.99m, Shipping = "Envío de 9,99 €" } },
            },
        };
        var summary = SummaryBuilder.Build(watchlist, history, 7, now);
        Assert.Contains("• A: 79,00 € · envío 9,99 €", summary);
        Assert.Contains("• B: 84,00 € · envío gratis", summary);

        var html = ReportGenerator.Build(watchlist, history, now);
        Assert.Contains("<th>Envío</th>", html);
        Assert.Contains("<td>9,99 €</td>", html);
    }

    [Fact]
    public void Extracts_shipping_cost_from_json_ld()
    {
        const string html = """
            <script type="application/ld+json">{"@type":"Product","offers":{"price":"100","priceCurrency":"EUR","shippingDetails":{"@type":"OfferShippingDetails","shippingRate":{"@type":"MonetaryAmount","value":"5.90","currency":"EUR"}}}}</script>
            """;
        Assert.Equal(5.90m, PriceExtractor.ExtractShippingCost(PriceExtractor.ParseDocument(html)));
        Assert.Null(PriceExtractor.ExtractShippingCost(PriceExtractor.ParseDocument("<p>nada</p>")));
    }
}
