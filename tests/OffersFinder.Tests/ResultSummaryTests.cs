using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class ResultSummaryTests
{
    [Fact]
    public void Lists_price_store_shipping_and_errors()
    {
        var bici = new WatchItem { Name = "Bici", Query = "bici" };
        var casco = new WatchItem { Name = "Casco", Query = "casco" };
        var offers = new List<Offer>
        {
            new("a", "Tienda A", "Bici", 159.95m, "https://a", false, "Envío gratis", 0),
            new("b", "Tienda B", "Bici", 179m, "https://b", true, null, null),
        };
        var results = new List<CheckResult>
        {
            new(bici, 159.95m, new PriceRecord { Price = 199m }, new(), null, offers),
            new(casco, null, null, new(), "Ninguna tienda devolvió resultados que encajen.", new()),
        };

        var text = Program.ResultSummary(results, new Settings());

        Assert.Contains("🛒 Bici\n   159,95 € en Tienda A · envío gratis (antes 199,00 €)\n   • Tienda B: 179,00 € · envío no indicado", text);
        Assert.Contains("🛒 Casco\n   Sin precio: Ninguna tienda devolvió resultados que encajen.", text);
    }
}
