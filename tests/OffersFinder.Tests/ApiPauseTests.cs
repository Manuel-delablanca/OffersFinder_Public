using OffersFinder.Models;
using Xunit;

namespace OffersFinder.Tests;

public class ApiPauseTests
{
    [Fact]
    public void Api_is_paused_before_the_date_and_active_from_it()
    {
        var paused = new Settings { SerpApiKey = "k", ApiPauseUntil = new DateOnly(2026, 11, 23), Today = new DateOnly(2026, 11, 10) };
        Assert.True(paused.ApiPaused);
        Assert.False(paused.UseShoppingSearch);
        Assert.False(paused.UseStoreSearch);

        var active = new Settings { SerpApiKey = "k", ApiPauseUntil = new DateOnly(2026, 11, 23), Today = new DateOnly(2026, 11, 23) };
        Assert.False(active.ApiPaused);
        Assert.True(active.UseShoppingSearch);

        Assert.False(new Settings { SerpApiKey = "k" }.ApiPaused);
    }

    [Fact]
    public void AllUrls_combines_url_and_urls_without_duplicates()
    {
        var item = new WatchItem { Url = "https://a/1", Urls = new() { "https://a/1", "https://b/2", "" } };
        Assert.Equal(new[] { "https://a/1", "https://b/2" }, item.AllUrls);
        Assert.Empty(new WatchItem { Query = "x" }.AllUrls);
    }
}
