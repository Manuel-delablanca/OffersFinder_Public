using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class ReportGeneratorTests
{
    private static PriceRecord At(string date, decimal price, string? store = null) =>
        new() { Timestamp = DateTimeOffset.Parse(date), Price = price, Store = store };

    [Fact]
    public void Renders_static_report_without_scripts()
    {
        var watchlist = new Watchlist
        {
            BlacklistedStores = { "amazon_es" },
            Items = { new WatchItem { Name = "Mi <producto>", Query = "x", TargetPrice = 150 } },
        };
        var history = new Dictionary<string, ItemHistory>
        {
            ["query:x"] = new ItemHistory
            {
                Records = { At("2026-10-20T10:00Z", 200, "Tienda A"), At("2026-11-05T10:00Z", 179, "Tienda B"), At("2026-11-07T10:00Z", 165, "Tienda B") },
                StoreRecords = new()
                {
                    new StoreRecord { Timestamp = DateTimeOffset.Parse("2026-11-07T10:00Z"), Store = "Tienda A", Price = 190 },
                    new StoreRecord { Timestamp = DateTimeOffset.Parse("2026-11-07T10:00Z"), Store = "Tienda B", Price = 165 },
                },
                LastOffersAt = DateTimeOffset.Parse("2026-11-07T10:00Z"),
                LastOffers = new() { new OfferRecord { Store = "Tienda B", Price = 165, Title = "Producto </script>", Url = "https://b/x?a=1&b=2", PickupMadrid = true } },
            },
        };

        var html = ReportGenerator.Build(watchlist, history, DateTimeOffset.UtcNow);

        Assert.DoesNotContain("<script", html);                 // nada de JavaScript ni CDN
        Assert.Contains("Mi &lt;producto&gt;", html);
        Assert.Contains("amazon_es", html);
        Assert.Contains("<svg class=\"chart\"", html);
        Assert.Contains("165,00 €", html);                      // mínimo / último
        Assert.Contains("200,00 €", html);                      // máximo
        Assert.Contains("objetivo 150,00 €", html);
        Assert.Contains("Tienda A", html);
        Assert.Contains("Producto &lt;/script&gt;", html);
        Assert.Contains("https://b/x?a=1&amp;b=2", html);
        Assert.Contains("recogida en Madrid", html);
        // Pestañas por mes: "Todos", octubre y noviembre, con la regla CSS que muestra cada panel
        Assert.Contains(">Todos<", html);
        Assert.Contains("octubre 2026", html);
        Assert.Contains("noviembre 2026", html);
        Assert.Contains("#i0-2026-11:checked ~ .panels .i0-2026-11{display:block}", html);
    }

    [Fact]
    public void Handles_items_without_history()
    {
        var watchlist = new Watchlist { Items = { new WatchItem { Name = "Nuevo", Query = "n" } } };
        var html = ReportGenerator.Build(watchlist, new Dictionary<string, ItemHistory>(), DateTimeOffset.UtcNow);
        Assert.Contains("Sin registros todavía", html);
    }

    [Fact]
    public void Svg_chart_draws_series_and_target()
    {
        var points = new[] { new ChartPoint(DateTimeOffset.Parse("2026-11-01T10:00Z"), 200), new ChartPoint(DateTimeOffset.Parse("2026-11-02T10:00Z"), 180) };
        var svg = SvgChart.Render(new[] { new ChartSeries("Mejor precio", points, "#2563eb", 3, false, true) }, 150, "€");
        Assert.Contains("<polyline", svg);
        Assert.Contains("<polygon", svg);
        Assert.Contains("objetivo 150,00 €", svg);
        Assert.Equal(2, svg.Split("<circle").Length - 1);
        Assert.Contains("Sin datos", SvgChart.Render(Array.Empty<ChartSeries>(), null, "€"));
    }
}
