using OffersFinder.Models;

namespace OffersFinder.Services;

public sealed record PriceStats(int Count, decimal Min, decimal Max, decimal Mean, decimal Median, PriceRecord First, PriceRecord Last)
{
    public decimal Change => Last.Price - First.Price;
}

/// <summary>Cálculo de estadísticas sobre el histórico de precios.</summary>
public static class Statistics
{
    public static PriceStats? Compute(IEnumerable<PriceRecord> records)
    {
        var list = records.OrderBy(r => r.Timestamp).ToList();
        if (list.Count == 0) return null;

        var prices = list.Select(r => r.Price).OrderBy(p => p).ToList();
        var median = prices.Count % 2 == 1
            ? prices[prices.Count / 2]
            : (prices[prices.Count / 2 - 1] + prices[prices.Count / 2]) / 2m;

        return new PriceStats(
            Count: list.Count,
            Min: prices[0],
            Max: prices[^1],
            Mean: Math.Round(prices.Average(), 2),
            Median: median,
            First: list[0],
            Last: list[^1]);
    }

    /// <summary>Filtra registros por mes ("2026-11"). Un valor nulo o vacío devuelve todos.</summary>
    public static IEnumerable<PriceRecord> FilterByMonth(IEnumerable<PriceRecord> records, string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return records;
        var parts = month.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var mon))
            throw new ArgumentException($"Mes no válido: '{month}'. Usa el formato AAAA-MM, por ejemplo 2026-11.");
        return records.Where(r =>
        {
            var local = r.Timestamp.ToLocalTime();
            return local.Year == year && local.Month == mon;
        });
    }

    public static IEnumerable<string> AvailableMonths(IEnumerable<PriceRecord> records) =>
        records.Select(r => r.Timestamp.ToLocalTime().ToString("yyyy-MM")).Distinct().OrderBy(m => m);
}
