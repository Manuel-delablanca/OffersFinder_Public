using System.Globalization;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Construye el resumen periódico: estado de cada producto en los últimos N días, una línea por dato.</summary>
public static class SummaryBuilder
{
    public static string Build(Watchlist watchlist, Dictionary<string, ItemHistory> history, int days, DateTimeOffset now)
    {
        var since = now.AddDays(-days);
        var blocks = new List<string>();

        foreach (var item in watchlist.Items)
        {
            history.TryGetValue(item.Key, out var h);
            var all = (h?.Records ?? new List<PriceRecord>()).OrderBy(r => r.Timestamp).ToList();
            var recent = all.Where(r => r.Timestamp >= since).ToList();
            var c = item.Currency;
            var lines = new List<string> { $"🛒 {item.DisplayName}" };

            if (recent.Count == 0)
            {
                lines.Add($"   Sin precio en los últimos {days} días");
                if (all.Count > 0)
                    lines.Add($"   Último conocido: {PriceParser.Format(all[^1].Price, c)} ({all[^1].Timestamp.ToLocalTime():d})");
                blocks.Add(string.Join("\n", lines));
                continue;
            }

            var last = recent[^1];
            var before = all.LastOrDefault(r => r.Timestamp < since) ?? recent[0];
            var change = last.Price - before.Price;
            var min = recent.Min(r => r.Price);

            lines.Add($"   Precio: {PriceParser.Format(last.Price, c)}{(last.Store is null ? "" : $" ({last.Store})")}");
            lines.Add(change == 0
                ? $"   Variación {days}d: sin cambios"
                : $"   Variación {days}d: {(change > 0 ? "▲ +" : "▼ -")}{PriceParser.Format(Math.Abs(change), c)} ({(change / before.Price * 100m).ToString("+0.#;-0.#", CultureInfo.InvariantCulture)} %)");
            lines.Add($"   Mínimo {days}d: {PriceParser.Format(min, c)}");
            if (item.TargetPrice is { } t)
                lines.Add(last.Price <= t
                    ? $"   🎯 Objetivo {PriceParser.Format(t, c)} alcanzado"
                    : $"   Objetivo: {PriceParser.Format(t, c)} (faltan {PriceParser.Format(last.Price - t, c)})");

            var stores = (h?.StoreRecords ?? new List<StoreRecord>())
                .Where(r => r.Timestamp >= since)
                .GroupBy(r => r.Store)
                .Select(g => (Store: g.Key, Price: g.OrderByDescending(r => r.Timestamp).First().Price))
                .OrderBy(x => x.Price)
                .Take(6)
                .ToList();
            if (stores.Count > 0)
            {
                lines.Add("   Tiendas:");
                foreach (var (store, price) in stores)
                    lines.Add($"   • {store}: {PriceParser.Format(price, c)}");
            }
            blocks.Add(string.Join("\n", lines));
        }

        if (blocks.Count == 0) blocks.Add("No hay productos en la lista.");
        return string.Join("\n\n", blocks);
    }
}
