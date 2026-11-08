using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class OfferSearcherTests
{
    private static Store StoreWithSelectors() => new()
    {
        Id = "tienda",
        Name = "Tienda",
        SearchUrl = "https://tienda.example/buscar?q={query}",
        PickupMadrid = true,
        Selectors = new StoreSelectors { Result = ".card", Title = ".title", Price = ".price", Link = "a" },
    };

    [Fact]
    public void Parses_results_with_store_selectors()
    {
        var html = """
            <div class="card"><a href="/p/1"><span class="title">Samsung 990 Pro 2TB</span></a><span class="price">159,99 €</span></div>
            <div class="card"><a href="/p/2"><span class="title">Samsung 990 Pro 4TB</span></a><span class="price">329,00 €</span></div>
            <div class="card"><a href="/p/3"><span class="title">Sin precio</span></a></div>
            """;
        var offers = OfferSearcher.ParseResults(html, StoreWithSelectors(), "https://tienda.example/buscar?q=x");

        Assert.Equal(2, offers.Count);
        Assert.Equal("Samsung 990 Pro 2TB", offers[0].Title);
        Assert.Equal(159.99m, offers[0].Price);
        Assert.Equal("https://tienda.example/p/1", offers[0].Url);
        Assert.True(offers[0].PickupMadrid);
    }

    [Fact]
    public void Parses_json_ld_item_list_when_selectors_fail()
    {
        var html = """
            <script type="application/ld+json">
            {"@type":"ItemList","itemListElement":[
              {"@type":"ListItem","item":{"@type":"Product","name":"Switch OLED","url":"https://tienda.example/switch","offers":{"@type":"Offer","price":"299.99"}}}
            ]}
            </script>
            """;
        var offers = OfferSearcher.ParseResults(html, StoreWithSelectors(), "https://tienda.example/buscar?q=x");

        var offer = Assert.Single(offers);
        Assert.Equal("Switch OLED", offer.Title);
        Assert.Equal(299.99m, offer.Price);
    }

    [Fact]
    public void Heuristic_finds_links_with_nearby_prices()
    {
        var store = new Store { Id = "s", Name = "S", SearchUrl = "https://s.example/?q={query}" };
        var html = """
            <ul>
              <li><div><a href="/producto-a">Auriculares Sony WH-1000XM5 negros</a></div><div>Precio 279,00 €</div></li>
              <li><div><a href="/producto-b">Funda para auriculares</a></div><div>9,99 €</div></li>
              <li><a href="/sin-precio">Ver más productos</a></li>
            </ul>
            """;
        var offers = OfferSearcher.ParseResults(html, store, "https://s.example/?q=x");

        Assert.Equal(2, offers.Count);
        Assert.Contains(offers, o => o.Title.Contains("WH-1000XM5") && o.Price == 279m);
    }

    [Fact]
    public void Matches_applies_keywords_exclusions_and_price_range()
    {
        var item = new WatchItem { Query = "Samsung 990 Pro 2TB", Exclude = new() { "4tb" }, MinPrice = 100, MaxPrice = 250 };
        Offer Make(string title, decimal price) => new("s", "S", title, price, "u", false);

        Assert.True(OfferSearcher.Matches(item, Make("SSD Samsung 990 PRO 2TB NVMe", 160)));
        Assert.False(OfferSearcher.Matches(item, Make("SSD Samsung 990 PRO 4TB NVMe", 160)), "excluido por palabra");
        Assert.False(OfferSearcher.Matches(item, Make("Samsung 980 Pro 2TB", 160)), "falta palabra");
        Assert.False(OfferSearcher.Matches(item, Make("Samsung 990 Pro 2TB", 20)), "por debajo del mínimo");
        Assert.False(OfferSearcher.Matches(item, Make("Samsung 990 Pro 2TB", 900)), "por encima del máximo");
    }

    [Fact]
    public void Matches_uses_must_contain_when_present()
    {
        var item = new WatchItem { Query = "Nintendo Switch OLED blanca", MustContain = new() { "switch", "oled" } };
        Assert.True(OfferSearcher.Matches(item, new("s", "S", "Consola Switch OLED", 300, "u", false)));
    }

    [Fact]
    public void StoresFor_filters_by_spain_madrid_and_blacklists()
    {
        var stores = new List<Store>
        {
            new() { Id = "envia", Name = "Envía", ShipsToSpain = true, PickupMadrid = false, SearchUrl = "https://envia.example/?q={query}" },
            new() { Id = "recoge", Name = "Recoge", ShipsToSpain = false, PickupMadrid = true, SearchUrl = "https://recoge.example/?q={query}" },
            new() { Id = "fuera", Name = "Fuera", ShipsToSpain = false, PickupMadrid = false, SearchUrl = "https://fuera.example/?q={query}" },
            new() { Id = "negra", Name = "Negra", ShipsToSpain = true, PickupMadrid = true, SearchUrl = "https://negra.example/?q={query}" },
            new() { Id = "apagada", Name = "Apagada", Enabled = false, ShipsToSpain = true, SearchUrl = "https://apagada.example/?q={query}" },
        };
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, stores, globalBlacklist: new[] { "negra" });

        var all = searcher.StoresFor(new WatchItem { Query = "x" }).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "envia", "recoge" }, all);

        var madrid = searcher.StoresFor(new WatchItem { Query = "x", PickupMadridOnly = true }).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "recoge" }, madrid);

        var perItem = searcher.StoresFor(new WatchItem { Query = "x", ExcludeStores = new() { "ENVIA" } }).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "recoge" }, perItem);

        var restricted = searcher.StoresFor(new WatchItem { Query = "x", Stores = new() { "envia" } }).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "envia" }, restricted);
    }

    [Fact]
    public void StoreForUrl_matches_by_host()
    {
        var stores = new List<Store> { new() { Id = "envia", Name = "Envía", SearchUrl = "https://www.envia.example/buscar?q={query}" } };
        using var fetcher = new PageFetcher(new Settings());
        var searcher = new OfferSearcher(fetcher, stores);

        Assert.Equal("envia", searcher.StoreForUrl("https://envia.example/producto/123")?.Id);
        Assert.Null(searcher.StoreForUrl("https://otra.example/producto"));
        Assert.Null(searcher.StoreForUrl("no es una url"));
    }
}
