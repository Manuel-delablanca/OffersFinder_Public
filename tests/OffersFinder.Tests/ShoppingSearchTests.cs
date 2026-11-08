using System.Net;
using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class ShoppingSearchTests
{
    private const string SampleJson = """
        {"searchParameters":{"q":"Samsung 990 Pro 2TB","gl":"es"},
         "shopping":[
           {"title":"Samsung 990 PRO 2TB SSD NVMe","source":"PcComponentes","link":"https://www.pccomponentes.com/x","price":"159,99 €","delivery":"Envío gratis","position":1},
           {"title":"Samsung 990 PRO 2TB con disipador","source":"Amazon.es","link":"https://www.amazon.es/dp/1","price":"189,00 €","position":2},
           {"title":"Samsung 990 PRO 4TB","source":"MediaMarkt","link":"https://www.mediamarkt.es/y","price":"329,00 €","position":3},
           {"title":"Samsung 990 PRO 2TB","source":"Tienda Rara","link":"https://tiendarara.example/z","price":"149,00 €","position":4},
           {"title":"Sin precio","source":"X","link":"https://x.example","position":5}
         ]}
        """;

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = SampleJson;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }

    private static List<Store> Catalog() => new()
    {
        new() { Id = "pccomponentes", Name = "PcComponentes", SearchUrl = "https://www.pccomponentes.com/buscar/?query={query}", PickupMadrid = true, Aliases = new() { "PC Componentes" } },
        new() { Id = "amazon_es", Name = "Amazon.es", SearchUrl = "https://www.amazon.es/s?k={query}", PickupMadrid = true },
        new() { Id = "mediamarkt", Name = "MediaMarkt", SearchUrl = "https://www.mediamarkt.es/es/search.html?query={query}", PickupMadrid = true },
    };

    [Fact]
    public void Parses_results_and_skips_entries_without_price()
    {
        var results = ShoppingSearchClient.Parse(SampleJson);
        Assert.Equal(4, results.Count);
        Assert.Equal(159.99m, results[0].Price);
        Assert.Equal("PcComponentes", results[0].Source);
        Assert.Equal("Envío gratis", results[0].Delivery);
    }

    [Fact]
    public async Task Sends_api_key_and_spanish_locale()
    {
        var handler = new FakeHandler();
        var client = new ShoppingSearchClient("KEY", new HttpClient(handler), "https://serper.example/shopping", "Spain");
        var results = await client.SearchAsync("Samsung 990 Pro 2TB");

        Assert.Equal(4, results.Count);
        Assert.Equal("KEY", handler.Last!.Headers.GetValues("X-API-KEY").Single());
        Assert.Contains("\"gl\":\"es\"", handler.Body);
        Assert.Contains("\"hl\":\"es\"", handler.Body);
        Assert.Contains("\"q\":\"Samsung 990 Pro 2TB\"", handler.Body);
    }

    [Fact]
    public async Task Throws_on_api_error()
    {
        var handler = new FakeHandler { Status = HttpStatusCode.Forbidden, Response = "{\"message\":\"Unauthorized\"}" };
        var client = new ShoppingSearchClient("KEY", new HttpClient(handler), "https://serper.example/shopping");
        var ex = await Assert.ThrowsAsync<FetchException>(() => client.SearchAsync("x"));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public void Store_name_matching_handles_aliases_accents_and_hosts()
    {
        var eci = new Store { Id = "elcorteingles", Name = "El Corte Inglés", SearchUrl = "https://www.elcorteingles.es/search-nwx/?s={query}" };
        Assert.True(eci.MatchesName("El Corte Ingles"));
        Assert.True(eci.MatchesName("elcorteingles.es"));
        Assert.False(eci.MatchesName("Amazon.es"));

        var pcc = Catalog()[0];
        Assert.True(pcc.MatchesName("PC Componentes"));
        Assert.True(pcc.MatchesName("pccomponentes.com"));
    }

    [Fact]
    public void Maps_results_to_catalog_and_applies_filters()
    {
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, Catalog(), globalBlacklist: new[] { "mediamarkt" });
        var item = new WatchItem { Query = "Samsung 990 Pro 2TB", Exclude = new() { "disipador" }, MaxPrice = 300 };

        var offers = searcher.OffersFromShopping(item, ShoppingSearchClient.Parse(SampleJson));

        // Amazon fuera por "disipador", MediaMarkt por lista negra; quedan PcComponentes y la tienda desconocida.
        Assert.Equal(2, offers.Count);
        var pcc = Assert.Single(offers, o => o.StoreId == "pccomponentes");
        Assert.True(pcc.PickupMadrid);
        var unknown = Assert.Single(offers, o => o.StoreName == "Tienda Rara");
        Assert.Equal("tiendarara", unknown.StoreId);
        Assert.False(unknown.PickupMadrid);
    }

    [Fact]
    public void PickupMadridOnly_keeps_only_catalog_stores_with_pickup()
    {
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, Catalog());
        var item = new WatchItem { Query = "Samsung 990 Pro 2TB", PickupMadridOnly = true };

        var offers = searcher.OffersFromShopping(item, ShoppingSearchClient.Parse(SampleJson));

        Assert.All(offers, o => Assert.True(o.PickupMadrid));
        Assert.DoesNotContain(offers, o => o.StoreName == "Tienda Rara");
    }

    [Fact]
    public async Task SearchAsync_uses_shopping_and_skips_stores_when_disabled()
    {
        var handler = new FakeHandler();
        var client = new ShoppingSearchClient("KEY", new HttpClient(handler), "https://serper.example/shopping");
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, Catalog(), shopping: client, useStores: false);

        var offers = await searcher.SearchAsync(new WatchItem { Query = "Samsung 990 Pro 2TB", MaxPrice = 300 });

        Assert.Equal(149m, offers[0].Price); // ordenadas por precio
        Assert.Equal(3, offers.Count);
    }
}

public class SerpApiProviderTests
{
    private const string SerpApiJson = """
        {"search_metadata":{"status":"Success"},
         "shopping_results":[
           {"position":1,"title":"Samsung 990 PRO 2TB","product_link":"https://www.google.com/shopping/product/1","link":"https://www.pccomponentes.com/x","source":"PcComponentes","price":"159,99 €","extracted_price":159.99},
           {"position":2,"title":"Samsung 990 PRO 2TB","link":"https://www.mediamarkt.es/y","source":"MediaMarkt","price":"169,00 €"}
         ]}
        """;

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public string Response = SerpApiJson;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Response) });
        }
    }

    [Fact]
    public async Task Uses_get_with_engine_and_api_key()
    {
        var handler = new FakeHandler();
        var client = new ShoppingSearchClient("SECRET", new HttpClient(handler), provider: ShoppingProvider.SerpApi);
        var results = await client.SearchAsync("Samsung 990 Pro 2TB");

        Assert.Equal(HttpMethod.Get, handler.Last!.Method);
        var query = System.Web.HttpUtility.ParseQueryString(handler.Last.RequestUri!.Query);
        Assert.Equal("google_shopping", query["engine"]);
        Assert.Equal("es", query["gl"]);
        Assert.Equal("SECRET", query["api_key"]);
        Assert.Equal("Samsung 990 Pro 2TB", query["q"]);

        Assert.Equal(2, results.Count);
        Assert.Equal(159.99m, results[0].Price);
        Assert.Equal(169m, results[1].Price);
        Assert.Equal("SerpApi", client.ProviderName);
    }

    [Fact]
    public void Provider_error_field_is_reported()
    {
        var ex = Assert.Throws<FetchException>(() => ShoppingSearchClient.Parse("{\"error\":\"Invalid API key\"}"));
        Assert.Contains("Invalid API key", ex.Message);
    }
}
