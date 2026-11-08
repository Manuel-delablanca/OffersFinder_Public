using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class StatisticsTests
{
    private static PriceRecord At(string date, decimal price) =>
        new() { Timestamp = DateTimeOffset.Parse(date), Price = price };

    [Fact]
    public void Computes_min_max_mean_median()
    {
        var records = new[] { At("2026-11-01T10:00Z", 100), At("2026-11-02T10:00Z", 80), At("2026-11-03T10:00Z", 120), At("2026-11-04T10:00Z", 90) };
        var stats = Statistics.Compute(records)!;

        Assert.Equal(4, stats.Count);
        Assert.Equal(80, stats.Min);
        Assert.Equal(120, stats.Max);
        Assert.Equal(97.5m, stats.Mean);
        Assert.Equal(95, stats.Median);
        Assert.Equal(100, stats.First.Price);
        Assert.Equal(90, stats.Last.Price);
        Assert.Equal(-10, stats.Change);
    }

    [Fact]
    public void Median_with_odd_count()
    {
        var stats = Statistics.Compute(new[] { At("2026-11-01T10:00Z", 5), At("2026-11-02T10:00Z", 1), At("2026-11-03T10:00Z", 3) })!;
        Assert.Equal(3, stats.Median);
    }

    [Fact]
    public void Returns_null_for_empty()
    {
        Assert.Null(Statistics.Compute(Array.Empty<PriceRecord>()));
    }

    [Fact]
    public void Filters_by_month()
    {
        var records = new[] { At("2026-10-31T12:00Z", 1), At("2026-11-15T12:00Z", 2), At("2026-12-01T12:00Z", 3) };
        var november = Statistics.FilterByMonth(records, "2026-11").ToList();
        Assert.Single(november);
        Assert.Equal(2, november[0].Price);
        Assert.Equal(3, Statistics.FilterByMonth(records, null).Count());
        Assert.Throws<ArgumentException>(() => Statistics.FilterByMonth(records, "noviembre").ToList());
    }

    [Fact]
    public void Lists_available_months()
    {
        var records = new[] { At("2026-12-01T12:00Z", 3), At("2026-10-31T12:00Z", 1), At("2026-10-05T12:00Z", 1) };
        Assert.Equal(new[] { "2026-10", "2026-12" }, Statistics.AvailableMonths(records));
    }
}
