using System.Globalization;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Construye el resumen periódico: estado de cada producto en los últimos N días.</summary>
public static class SummaryBuilder
{
    public static string Build(Watchlist watchlist, Dictionary<string, ItemHistory> history, int days, DateTimeOffset now)
    {
        var since = now.AddDays(-days);
        var lines = new List<string>();

        foreach (var item in watchlist.Items)
        {
            history.TryGetValue(item.Key, out var h);
            var all = (h?.Records ?? new List<PriceRecord>()).OrderBy(r => r.Timestamp).ToList();
            var recent = all.Where(r => r.Timestamp >= since).ToList();
            var c = item.Currency;

            if (recent.Count == 0)
            {
                lines.Add($"• {item.DisplayName}: sin precio en los últimos {days} días" +
                          (all.Count > 0 ? $" (último conocido {PriceParser.Format(all[^1].Price, c)} el {all[^1].Timestamp.ToLocalTime():d})" : ""));
                continue;
            }

            var last = recent[^1];
            var before = all.LastOrDefault(r => r.Timestamp < since) ?? recent[0];
            var change = last.Price - before.Price;
            var changeText = change == 0 ? "sin cambios" : $"{(change > 0 ? "▲" : "▼")} {PriceParser.Format(Math.Abs(change), c)} ({(change / before.Price * 100m).ToString("+0.#;-0.#", CultureInfo.InvariantCulture)} %)";
            var min = recent.Min(r => r.Price);
            var target = item.TargetPrice is { } t
                ? (last.Price <= t ? $" · 🎯 por debajo del objetivo {PriceParser.Format(t, c)}" : $" · objetivo {PriceParser.Format(t, c)} (faltan {PriceParser.Format(last.Price - t, c)})")
                : "";

            lines.Add($"• {item.DisplayName}: {PriceParser.Format(last.Price, c)}{(last.Store is null ? "" : $" en {last.Store}")} · {changeText} · mín. {days}d {PriceParser.Format(min, c)}{target}");

            var stores = (h?.StoreRecords ?? new List<StoreRecord>())
                .Where(r => r.Timestamp >= since)
                .GroupBy(r => r.Store)
                .Select(g => (Store: g.Key, Price: g.OrderByDescending(r => r.Timestamp).First().Price))
                .OrderBy(x => x.Price)
                .Take(4)
                .ToList();
            if (stores.Count > 1)
                lines.Add("    " + string.Join(" · ", stores.Select(x => $"{x.Store} {PriceParser.Format(x.Price, c)}")));
        }

        if (lines.Count == 0) lines.Add("No hay productos en la lista.");
        return string.Join("\n", lines);
    }
}
