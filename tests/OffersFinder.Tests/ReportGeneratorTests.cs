using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class ReportGeneratorTests
{
    [Fact]
    public void Embeds_items_history_and_blacklist()
    {
        var watchlist = new Watchlist
        {
            BlacklistedStores = { "amazon_es" },
            Items = { new WatchItem { Name = "Mi <producto>", Query = "x", Url = "https://x/</script>" } },
        };
        var history = new Dictionary<string, ItemHistory>
        {
            ["https://x/</script>"] = new ItemHistory { Records = { new PriceRecord { Price = 12.5m, Timestamp = DateTimeOffset.UtcNow, Store = "Tienda" } } },
        };

        var html = ReportGenerator.Build(watchlist, history, DateTimeOffset.UtcNow);

        Assert.Contains("Mi <producto>", html);
        Assert.Contains("\"amazon_es\"", html);
        Assert.Contains("12.5", html);
        Assert.Contains("chart.umd.js", html);
        Assert.DoesNotContain("https://x/</script>", html); // la etiqueta se escapa para no romper el documento
    }

    [Fact]
    public void Embeds_last_offers()
    {
        var watchlist = new Watchlist { Items = { new WatchItem { Name = "Bici", Query = "bici" } } };
        var history = new Dictionary<string, ItemHistory>
        {
            ["query:bici"] = new ItemHistory
            {
                LastOffersAt = DateTimeOffset.UtcNow,
                LastOffers = new() { new OfferRecord { Store = "Tienda A", Price = 179m, Title = "Bici roja", Url = "https://a/b", PickupMadrid = true } },
            },
        };
        var html = ReportGenerator.Build(watchlist, history, DateTimeOffset.UtcNow);
        Assert.Contains("\"lastOffers\":[{\"store\":\"Tienda A\"", html);
        Assert.Contains("\"pickup\":true", html);
    }
}
