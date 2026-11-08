using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class PriceCheckerTests
{
    private static PriceChecker Checker(decimal minDrop = 0) =>
        new(new Settings { MinDropPercent = minDrop }, new PageFetcher(new Settings()), new OfferSearcher(new PageFetcher(new Settings()), new List<Store>()), _ => { });

    private static PriceRecord Record(decimal price, int daysAgo = 1) =>
        new() { Price = price, Timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo) };

    [Fact]
    public void No_alert_on_first_observation()
    {
        var item = new WatchItem { Name = "X", Url = "https://x", TargetPrice = 50 };
        var alerts = Checker().Evaluate(item, new ItemHistory(), 100, previous: null, lowestBefore: null, new()).ToList();
        Assert.Empty(alerts);
    }

    [Fact]
    public void Alerts_on_price_drop_and_marks_lowest()
    {
        var item = new WatchItem { Name = "X", Url = "https://x" };
        var history = new ItemHistory { Records = { Record(120, 2), Record(110, 1) } };
        var alerts = Checker().Evaluate(item, history, 99, history.Last, history.LowestEver, new()).ToList();

        var alert = Assert.Single(alerts);
        Assert.StartsWith("⬇️", alert.Title);
        Assert.Contains("-10 %", alert.Body);
        Assert.Contains("más bajo", alert.Body);
    }

    [Fact]
    public void Respects_minimum_drop_percent()
    {
        var item = new WatchItem { Name = "X", Url = "https://x" };
        var history = new ItemHistory { Records = { Record(100) } };
        Assert.Empty(Checker(minDrop: 5).Evaluate(item, history, 98, history.Last, 100, new()));
        Assert.Single(Checker(minDrop: 5).Evaluate(item, history, 90, history.Last, 100, new()));
    }

    [Fact]
    public void No_drop_alert_when_disabled_or_price_rises()
    {
        var history = new ItemHistory { Records = { Record(100) } };
        Assert.Empty(Checker().Evaluate(new WatchItem { Url = "https://x", AlertOnDrop = false }, history, 50, history.Last, 100, new()));
        Assert.Empty(Checker().Evaluate(new WatchItem { Url = "https://x" }, history, 150, history.Last, 100, new()));
    }

    [Fact]
    public void Target_alert_fires_once_until_price_rises_again()
    {
        var item = new WatchItem { Name = "X", Url = "https://x", TargetPrice = 100, AlertOnDrop = false };
        var history = new ItemHistory();
        var checker = Checker();

        Assert.Single(checker.Evaluate(item, history, 95, null, null, new()));   // primera vez bajo el objetivo
        Assert.Empty(checker.Evaluate(item, history, 95, null, null, new()));    // mismo precio: no repetir
        Assert.Single(checker.Evaluate(item, history, 90, null, null, new()));   // baja aún más: avisar
        Assert.Empty(checker.Evaluate(item, history, 120, null, null, new()));   // sube por encima: se reinicia
        Assert.Single(checker.Evaluate(item, history, 99, null, null, new()));   // vuelve a bajar: avisar
    }

    [Fact]
    public void Alert_lists_best_offers_with_pickup_info()
    {
        var item = new WatchItem { Name = "X", Query = "x", TargetPrice = 100, AlertOnDrop = false };
        var offers = new List<Offer>
        {
            new("a", "Tienda A", "Producto X", 90, "https://a/x", true),
            new("b", "Tienda B", "Producto X", 95, "https://b/x", false),
        };
        var alert = Assert.Single(Checker().Evaluate(item, new ItemHistory(), 90, null, null, offers));
        Assert.Contains("Tienda A (recogida en Madrid)", alert.Body);
        Assert.Contains("Tienda B:", alert.Body);
        Assert.Contains("https://a/x", alert.Body);
        Assert.Null(alert.Url);
    }
}
