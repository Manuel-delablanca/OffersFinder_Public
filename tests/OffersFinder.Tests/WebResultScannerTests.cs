using System.Net;
using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class WebResultScannerTests
{
    private sealed class FakeWeb : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Pages { get; } = new();
        public List<string> Requested { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            if (!Pages.TryGetValue(url, out var page)) page = (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(page.Status) { Content = new StringContent(page.Body) });
        }
    }

    private const string ProductPage = """
        <html><head><title>Orbea MX 12 2025 | Bicis Pepe</title>
        <script type="application/ld+json">{"@type":"Product","name":"Orbea MX 12 Azul","offers":{"@type":"Offer","price":"189.00","priceCurrency":"EUR"}}</script>
        </head><body></body></html>
        """;

    private const string ListingPage = """<html><head><title>Bicicletas infantiles</title></head><body><div class="price">99 €</div></body></html>""";

    private static List<WebResult> Results() => new()
    {
        new("Orbea MX 12 | Bicis Pepe", "https://bicispepe.example/orbea-mx-12", null),
        new("Bicicletas infantiles", "https://otratienda.example/infantil", null),
        new("Orbea MX 12 - Amazon", "https://www.amazon.es/dp/123", null),
        new("Orbea MX 12 opiniones - Forocoches", "https://www.forocoches.com/foro/123", null),
        new("Orbea MX 12 | Tienda Bloqueada", "https://bloqueada.example/mx12", null),
        new("Orbea MX 12 | Vetada", "https://vetada.example/mx12", null),
    };

    [Fact]
    public async Task Reads_pages_extracts_structured_prices_and_skips_the_rest()
    {
        var web = new FakeWeb();
        web.Pages["https://bicispepe.example/orbea-mx-12"] = (HttpStatusCode.OK, ProductPage);
        web.Pages["https://otratienda.example/infantil"] = (HttpStatusCode.OK, ListingPage);
        web.Pages["https://bloqueada.example/mx12"] = (HttpStatusCode.Forbidden, "blocked");
        using var fetcher = new PageFetcher(new Settings(), retries: 1, handler: web);
        var scanner = new WebResultScanner(fetcher, new List<Store>(), globalBlacklist: new[] { "vetada" });
        var item = new WatchItem { Query = "Orbea MX 12", MaxPrice = 400 };

        var pages = await scanner.ScanAsync(item, Results());

        var offer = Assert.Single(pages.Where(p => p.Offer is not null)).Offer!;
        Assert.Equal(189m, offer.Price);
        Assert.Equal("Orbea MX 12 Azul", offer.Title);
        Assert.Equal("bicispepe", offer.StoreId);
        Assert.Equal("bicispepe.example", offer.StoreName);

        Assert.Contains(pages, p => p.Url.Contains("otratienda") && p.Outcome.StartsWith("sin precio estructurado"));
        Assert.Contains(pages, p => p.Url.Contains("amazon") && p.Outcome.StartsWith("omitida"));
        Assert.Contains(pages, p => p.Url.Contains("forocoches") && p.Outcome.StartsWith("omitida"));
        Assert.Contains(pages, p => p.Url.Contains("bloqueada") && p.Outcome.Contains("bloquea"));
        Assert.Contains(pages, p => p.Url.Contains("vetada") && p.Outcome == "tienda en lista negra");
        Assert.DoesNotContain(web.Requested, u => u.Contains("amazon") || u.Contains("forocoches") || u.Contains("vetada"));
    }

    [Fact]
    public async Task Uses_catalog_store_for_pickup_and_rejects_when_pickup_required()
    {
        var web = new FakeWeb();
        web.Pages["https://bicispepe.example/orbea-mx-12"] = (HttpStatusCode.OK, ProductPage);
        using var fetcher = new PageFetcher(new Settings(), retries: 1, handler: web);
        var catalog = new List<Store> { new() { Id = "pepe", Name = "Bicis Pepe", SearchUrl = "https://www.bicispepe.example/buscar?q={query}", PickupMadrid = true } };
        var scanner = new WebResultScanner(fetcher, catalog);
        var results = new List<WebResult> { new("Orbea", "https://bicispepe.example/orbea-mx-12", null), new("Orbea", "https://otra.example/p", null) };

        var pages = await scanner.ScanAsync(new WatchItem { Query = "Orbea MX 12", PickupMadridOnly = true }, results);

        var ok = Assert.Single(pages, p => p.Offer is not null);
        Assert.Equal("pepe", ok.Offer!.StoreId);
        Assert.True(ok.Offer.PickupMadrid);
        Assert.Contains(pages, p => p.Url.Contains("otra.example") && p.Outcome == "sin recogida en Madrid");
    }

    [Fact]
    public async Task Respects_page_limit()
    {
        var web = new FakeWeb();
        var results = Enumerable.Range(1, 6).Select(i =>
        {
            var url = $"https://tienda{i}.example/p";
            web.Pages[url] = (HttpStatusCode.OK, ProductPage);
            return new WebResult("Orbea MX 12", url, null);
        }).ToList();
        using var fetcher = new PageFetcher(new Settings(), retries: 1, handler: web);
        var scanner = new WebResultScanner(fetcher, new List<Store>(), maxPages: 3);

        var pages = await scanner.ScanAsync(new WatchItem { Query = "Orbea MX 12" }, results);

        Assert.Equal(3, web.Requested.Count);
        Assert.Equal(3, pages.Count(p => p.Offer is not null));
        Assert.Equal(3, pages.Count(p => p.Outcome.Contains("límite")));
    }

    [Fact]
    public async Task Falls_back_to_css_price_only_when_page_title_matches_product()
    {
        var web = new FakeWeb();
        web.Pages["https://a.example/strider"] = (HttpStatusCode.OK, """<html><head><title>Strider 12 Sport – Tienda A</title></head><body><span class="product-price">129,00 €</span></body></html>""");
        web.Pages["https://b.example/listado"] = (HttpStatusCode.OK, """<html><head><title>Bicicletas infantiles – Tienda B</title></head><body><span class="product-price">129,00 €</span></body></html>""");
        using var fetcher = new PageFetcher(new Settings(), retries: 1, handler: web);
        var scanner = new WebResultScanner(fetcher, new List<Store>());
        var item = new WatchItem { Query = "Strider 12", MustContain = new() { "strider", "12" } };

        var pages = await scanner.ScanAsync(item, new List<WebResult> { new("Strider 12", "https://a.example/strider", null), new("Listado", "https://b.example/listado", null) });

        var ok = Assert.Single(pages, p => p.Offer is not null);
        Assert.Equal(129m, ok.Offer!.Price);
        Assert.Contains("precio leído del texto", ok.Offer.Title);
        Assert.Contains(pages, p => p.Url.Contains("b.example") && p.Outcome.StartsWith("sin precio estructurado"));
    }

    [Fact]
    public async Task Rejects_pages_priced_in_other_currencies()
    {
        const string chile = """
            <html><head><title>Strider 12 Sport</title>
            <script type="application/ld+json">{"@type":"Product","name":"Strider 12 Sport","offers":{"@type":"Offer","price":"101550","priceCurrency":"CLP"}}</script>
            </head></html>
            """;
        const string usa = """
            <html><head><title>Strider 12 Sport</title></head><body><span class="price">$129.99</span></body></html>
            """;
        const string spain = """
            <html><head><title>Strider 12 Sport</title>
            <script type="application/ld+json">{"@type":"Product","name":"Strider 12 Sport","offers":{"@type":"Offer","price":"129.00","priceCurrency":"EUR"}}</script>
            </head></html>
            """;
        var web = new FakeWeb();
        web.Pages["https://chile.example/strider"] = (HttpStatusCode.OK, chile);
        web.Pages["https://usa.example/strider"] = (HttpStatusCode.OK, usa);
        web.Pages["https://es.example/strider"] = (HttpStatusCode.OK, spain);
        using var fetcher = new PageFetcher(new Settings(), retries: 1, handler: web);
        var scanner = new WebResultScanner(fetcher, new List<Store>());
        var item = new WatchItem { Query = "Strider 12" };
        var results = new List<WebResult> { new("a", "https://chile.example/strider", null), new("b", "https://usa.example/strider", null), new("c", "https://es.example/strider", null) };

        var pages = await scanner.ScanAsync(item, results);

        var ok = Assert.Single(pages, p => p.Offer is not null);
        Assert.Equal("https://es.example/strider", ok.Url);
        Assert.Contains(pages, p => p.Url.Contains("chile") && p.Outcome.Contains("CLP"));
        Assert.Contains(pages, p => p.Url.Contains("usa") && p.Outcome.Contains("euros"));
    }

    [Fact]
    public void Extracts_declared_currency()
    {
        const string meta = """<meta property="product:price:currency" content="eur">""";
        const string jsonLd = """<script type="application/ld+json">{"@type":"Product","offers":[{"price":1,"priceCurrency":"USD"}]}</script>""";
        Assert.Equal("EUR", PriceExtractor.ExtractCurrency(PriceExtractor.ParseDocument(meta)));
        Assert.Equal("USD", PriceExtractor.ExtractCurrency(PriceExtractor.ParseDocument(jsonLd)));
        Assert.Null(PriceExtractor.ExtractCurrency(PriceExtractor.ParseDocument("<html><body>nada</body></html>")));
    }

    [Fact]
    public void Parses_organic_results_from_both_providers()
    {
        var serper = ShoppingSearchClient.ParseWeb("""{"organic":[{"title":"A","link":"https://a.example","snippet":"s"},{"title":"B","link":"javascript:void(0)"}]}""");
        var serpapi = ShoppingSearchClient.ParseWeb("""{"organic_results":[{"position":1,"title":"C","link":"https://c.example","snippet":"x"}]}""");
        Assert.Single(serper);
        Assert.Equal("https://a.example", serper[0].Url);
        Assert.Single(serpapi);
        Assert.Equal("C", serpapi[0].Title);
    }

    [Fact]
    public void Product_name_prefers_json_ld_then_og_title_then_title()
    {
        var withJsonLd = PriceExtractor.ParseDocument(ProductPage);
        Assert.Equal("Orbea MX 12 Azul", PriceExtractor.ProductName(withJsonLd));
        var withOg = PriceExtractor.ParseDocument("""<html><head><title>T</title><meta property="og:title" content="OG"></head></html>""");
        Assert.Equal("OG", PriceExtractor.ProductName(withOg));
        var onlyTitle = PriceExtractor.ParseDocument("<html><head><title>Solo título</title></head></html>");
        Assert.Equal("Solo título", PriceExtractor.ProductName(onlyTitle));
    }
}
