using System.Net;
using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class LearnedUrlsTests
{
    private sealed class FakeWeb : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode, string)> Pages { get; } = new();
        public List<string> Requested { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            var (status, body) = Pages.TryGetValue(url, out var p) ? p : (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private const string Page = """<html><head><title>Molinillo HiBREW G3</title><script type="application/ld+json">{"@type":"Product","name":"HiBREW G3","offers":{"price":"78.00","priceCurrency":"EUR"}}</script></head></html>""";

    [Fact]
    public void Learns_direct_store_links_but_not_google_links()
    {
        var h = new ItemHistory();
        var now = DateTimeOffset.UtcNow;
        PriceChecker.LearnUrls(h, new[]
        {
            new Offer("a", "Tienda A", "G3", 79, "https://tienda-a.es/g3", false),
            new Offer("g", "LaFuga", "G3", 75, "https://www.google.com/search?ibp=oshop&prds=1", false),
        }, now);
        var learned = Assert.Single(h.LearnedUrls!);
        Assert.Equal("https://tienda-a.es/g3", learned.Url);
        Assert.Equal(79, learned.LastPrice);
    }

    [Fact]
    public async Task Reads_learned_urls_when_api_is_paused_and_drops_them_after_three_failures()
    {
        var web = new FakeWeb();
        web.Pages["https://tienda-a.es/g3"] = (HttpStatusCode.OK, Page);
        var settings = new Settings { SerperApiKey = "k", ApiPauseUntil = new DateOnly(2026, 11, 23), Today = new DateOnly(2026, 11, 10) };
        using var fetcher = new PageFetcher(settings, retries: 1, handler: web);
        var searcher = new OfferSearcher(fetcher, new List<Store>(), shopping: new ShoppingSearchClient("k", new HttpClient(web), "https://serper.example/x"), useStores: false);
        var checker = new PriceChecker(settings, fetcher, searcher, _ => { });
        var item = new WatchItem { Name = "G3", Query = "HiBREW G3" };
        var now = DateTimeOffset.UtcNow;
        var history = new Dictionary<string, ItemHistory>
        {
            [item.Key] = new ItemHistory
            {
                LastApiSearchAt = now.AddDays(-1),
                LearnedUrls = new()
                {
                    new LearnedUrl { Url = "https://tienda-a.es/g3", Store = "Tienda A", LastPrice = 79, LastSeen = now.AddDays(-1) },
                    new LearnedUrl { Url = "https://caida.es/g3", Store = "Caída", LastPrice = 70, LastSeen = now.AddDays(-1), Failures = 2 },
                },
            },
        };

        var result = await checker.CheckAsync(item, history, now);

        Assert.Equal(78m, result.Price);                                  // leído de la ficha aprendida, sin API
        Assert.DoesNotContain(web.Requested, u => u.Contains("serper.example"));
        var remaining = Assert.Single(history[item.Key].LearnedUrls!);   // la que falló por tercera vez se descartó
        Assert.Equal("https://tienda-a.es/g3", remaining.Url);
        Assert.Equal(0, remaining.Failures);
        Assert.Equal(78m, remaining.LastPrice);
    }
}
