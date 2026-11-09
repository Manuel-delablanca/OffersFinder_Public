using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class SummaryBuilderTests
{
    private static PriceRecord At(DateTimeOffset t, decimal price, string? store = null) => new() { Timestamp = t, Price = price, Store = store };

    [Fact]
    public void Summarizes_change_min_target_and_stores()
    {
        var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        var watchlist = new Watchlist { Items = { new WatchItem { Name = "Bici", Query = "bici", TargetPrice = 150 } } };
        var history = new Dictionary<string, ItemHistory>
        {
            ["query:bici"] = new ItemHistory
            {
                Records = { At(now.AddDays(-10), 200, "A"), At(now.AddDays(-5), 190, "A"), At(now.AddDays(-1), 179, "B") },
                StoreRecords = new()
                {
                    new StoreRecord { Timestamp = now.AddDays(-1), Store = "A", Price = 190 },
                    new StoreRecord { Timestamp = now.AddDays(-1), Store = "B", Price = 179 },
                },
            },
        };

        var text = SummaryBuilder.Build(watchlist, history, 7, now);

        Assert.Contains("🛒 Bici\n   Precio: 179,00 € (B)", text);
        Assert.Contains("Variación 7d: ▼ -21,00 € (-10.5 %)", text);   // respecto al último precio anterior a la ventana (200)
        Assert.Contains("Mínimo 7d: 179,00 €", text);
        Assert.Contains("Objetivo: 150,00 € (faltan 29,00 €)", text);
        Assert.Contains("   • B: 179,00 €\n   • A: 190,00 €", text);
    }

    [Fact]
    public void Reports_items_without_recent_prices()
    {
        var now = DateTimeOffset.UtcNow;
        var watchlist = new Watchlist { Items = { new WatchItem { Name = "Vieja", Query = "v" }, new WatchItem { Name = "Nueva", Query = "n" } } };
        var history = new Dictionary<string, ItemHistory> { ["query:v"] = new ItemHistory { Records = { At(now.AddDays(-30), 99) } } };

        var text = SummaryBuilder.Build(watchlist, history, 7, now);

        Assert.Contains("🛒 Vieja\n   Sin precio en los últimos 7 días\n   Último conocido: 99,00 €", text);
        Assert.Contains("🛒 Nueva\n   Sin precio en los últimos 7 días", text);
    }
}
