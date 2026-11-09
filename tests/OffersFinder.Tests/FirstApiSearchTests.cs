using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class FirstApiSearchTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            const string json = """{"shopping":[{"title":"Casco Micro Flamenco S","source":"Tienda","link":"https://t/x","price":"49,90 €"}]}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Fact]
    public async Task New_item_is_searched_once_even_when_api_is_paused()
    {
        var handler = new FakeHandler();
        var shopping = new ShoppingSearchClient("k", new HttpClient(handler), "https://serper.example/shopping");
        var settings = new Settings { SerperApiKey = "k", ApiPauseUntil = new DateOnly(2026, 11, 23), Today = new DateOnly(2026, 11, 10) };
        Assert.True(settings.ApiPaused);
        using var fetcher = new PageFetcher(settings);
        var searcher = new OfferSearcher(fetcher, new List<Store>(), shopping: shopping, useStores: false);
        var checker = new PriceChecker(settings, fetcher, searcher, _ => { });
        var item = new WatchItem { Name = "Casco", Query = "Casco Micro Flamenco" };
        var history = new Dictionary<string, ItemHistory>();
        var now = DateTimeOffset.UtcNow;

        var first = await checker.CheckAsync(item, history, now);
        Assert.Equal(49.90m, first.Price);
        Assert.Equal(1, handler.Calls);
        Assert.NotNull(history[item.Key].LastApiSearchAt);

        var second = await checker.CheckAsync(item, history, now.AddDays(1));
        Assert.Equal(1, handler.Calls);           // en pausa y ya buscado: no se vuelve a consultar
        Assert.Null(second.Price);
        Assert.Contains("API en pausa", second.Error);
    }
}
