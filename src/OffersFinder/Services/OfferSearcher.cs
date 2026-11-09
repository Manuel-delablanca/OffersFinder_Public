using System.Text.Json;
using AngleSharp.Dom;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Busca un producto en las tiendas del catálogo y devuelve las ofertas que encajan.</summary>
public sealed class OfferSearcher
{
    private readonly PageFetcher _fetcher;
    private readonly IReadOnlyList<Store> _stores;
    private readonly IReadOnlyCollection<string> _globalBlacklist;
    private readonly Action<string> _log;
    private readonly ShoppingSearchClient? _shopping;
    private readonly bool _useStores;
    private readonly WebResultScanner? _webScanner;

    public OfferSearcher(PageFetcher fetcher, IReadOnlyList<Store> stores, IReadOnlyCollection<string>? globalBlacklist = null, Action<string>? log = null,
        ShoppingSearchClient? shopping = null, bool useStores = true, WebResultScanner? webScanner = null)
    {
        _fetcher = fetcher;
        _stores = stores;
        _globalBlacklist = globalBlacklist ?? Array.Empty<string>();
        _log = log ?? (_ => { });
        _shopping = shopping;
        _useStores = useStores;
        _webScanner = webScanner;
    }

    /// <summary>Busca en Google (web), lee las páginas de resultados y devuelve lo que pasó con cada una.</summary>
    public async Task<List<ScannedPage>> ScanWebAsync(WatchItem item, CancellationToken ct = default)
    {
        if (_shopping is null || _webScanner is null || !item.HasQuery) return new List<ScannedPage>();
        var results = await _shopping.WebSearchAsync(item.Query!, ct);
        return await _webScanner.ScanAsync(item, results, ct);
    }

    /// <summary>Tienda del catálogo que corresponde a un nombre dado por un agregador, si la hay.</summary>
    public Store? StoreForName(string? sourceName) => _stores.FirstOrDefault(s => s.MatchesName(sourceName));

    /// <summary>Convierte resultados de Google Shopping en ofertas, aplicando catálogo, listas negras y filtros del producto.</summary>
    public List<Offer> OffersFromShopping(WatchItem item, IEnumerable<ShoppingResult> results)
    {
        var offers = new List<Offer>();
        foreach (var r in results)
        {
            var store = StoreForName(r.Source);
            var storeId = store?.Id ?? Store.Normalize(r.Source);
            var storeName = store?.Name ?? r.Source;

            if (store is not null && !store.Enabled) continue;
            if (_globalBlacklist.Contains(storeId, StringComparer.OrdinalIgnoreCase)) continue;
            if (item.ExcludeStores is not null && item.ExcludeStores.Contains(storeId, StringComparer.OrdinalIgnoreCase)) continue;
            if (item.Stores is { Count: > 0 } && !item.Stores.Contains(storeId, StringComparer.OrdinalIgnoreCase)) continue;

            var pickup = store?.PickupMadrid ?? false;
            if (item.PickupMadridOnly && !pickup) continue;

            var offer = new Offer(storeId, storeName, r.Title, r.Price, r.Url, pickup);
            if (Matches(item, offer)) offers.Add(offer);
        }
        return offers;
    }

    /// <summary>
    /// Tiendas aplicables a un producto: envían a España o permiten recogida en Madrid,
    /// no están en la lista negra global ni en la del producto, y cumplen sus restricciones.
    /// </summary>
    public IEnumerable<Store> StoresFor(WatchItem item) =>
        _stores.Where(s => s.Enabled)
               .Where(s => s.ShipsToSpain || s.PickupMadrid)
               .Where(s => !_globalBlacklist.Contains(s.Id, StringComparer.OrdinalIgnoreCase))
               .Where(s => item.ExcludeStores is null || !item.ExcludeStores.Contains(s.Id, StringComparer.OrdinalIgnoreCase))
               .Where(s => !item.PickupMadridOnly || s.PickupMadrid)
               .Where(s => item.Stores is null || item.Stores.Count == 0 ||
                           item.Stores.Contains(s.Id, StringComparer.OrdinalIgnoreCase));

    /// <summary>Tienda del catálogo a la que pertenece una URL (por dominio), si hay alguna.</summary>
    public Store? StoreForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = StripWww(uri.Host);
        return _stores.FirstOrDefault(s =>
            Uri.TryCreate(s.SearchUrl, UriKind.Absolute, out var storeUri) &&
            string.Equals(StripWww(storeUri.Host), host, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsBlacklisted(Store store, WatchItem item) =>
        _globalBlacklist.Contains(store.Id, StringComparer.OrdinalIgnoreCase) ||
        (item.ExcludeStores?.Contains(store.Id, StringComparer.OrdinalIgnoreCase) ?? false);

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    public async Task<List<Offer>> SearchAsync(WatchItem item, CancellationToken ct = default)
    {
        var offers = new List<Offer>();
        if (!item.HasQuery) return offers;

        if (_shopping is not null)
        {
            try
            {
                var results = await _shopping.SearchAsync(item.Query!, ct);
                var matching = OffersFromShopping(item, results);
                _log($"  · Google Shopping vía {_shopping.ProviderName}: {results.Count} resultados, {matching.Count} encajan");
                offers.AddRange(matching);
            }
            catch (FetchException ex)
            {
                _log($"  · Google Shopping: {ex.Message}");
            }

            if (_webScanner is not null)
            {
                try
                {
                    var pages = await ScanWebAsync(item, ct);
                    var found = pages.Where(p => p.Offer is not null).Select(p => p.Offer!).ToList();
                    var read = pages.Count(p => p.Outcome == "✓" || p.Outcome.StartsWith("no encaja") || p.Outcome.StartsWith("sin precio") || p.Outcome.Contains("bloquea"));
                    _log($"  · Google (web) vía {_shopping.ProviderName}: {pages.Count} resultados, {read} páginas leídas, {found.Count} con precio que encaja");
                    offers.AddRange(found);
                }
                catch (FetchException ex)
                {
                    _log($"  · Google (web): {ex.Message}");
                }
            }
        }

        foreach (var store in _useStores ? StoresFor(item) : Enumerable.Empty<Store>())
        {
            var url = store.BuildSearchUrl(item.Query!);
            try
            {
                var html = await _fetcher.GetHtmlAsync(url, MergeHeaders(store.Headers, item.Headers), ct);
                var found = ParseResults(html, store, url);
                var matching = found.Where(o => Matches(item, o)).ToList();
                _log($"  · {store.Name}: {found.Count} resultados, {matching.Count} encajan");
                offers.AddRange(matching);
            }
            catch (FetchException ex)
            {
                _log($"  · {store.Name}: {ex.Message}");
            }
        }

        return offers
            .GroupBy(o => o.Url)
            .Select(g => g.OrderBy(o => o.Price).First())
            .OrderBy(o => o.Price)
            .ToList();
    }

    /// <summary>Comprueba que la oferta encaja con las palabras y el rango de precio del producto.</summary>
    public static bool Matches(WatchItem item, Offer offer)
    {
        if (TextMatch.MissingTerms(offer.Title, TextMatch.RequiredTerms(item)).Count > 0) return false;
        if (TextMatch.FirstExcluded(offer.Title, item.Exclude) is not null) return false;
        if (item.MaxPrice is { } max && offer.Price > max) return false;
        if (item.MinPrice is { } min && offer.Price < min) return false;
        return true;
    }

    /// <summary>Extrae ofertas de una página de resultados: selectores de la tienda, JSON-LD ItemList o heurística.</summary>
    public static List<Offer> ParseResults(string html, Store store, string pageUrl)
    {
        var document = PriceExtractor.ParseDocument(html);
        var baseUri = new Uri(pageUrl);

        var results = new List<Offer>();
        if (store.Selectors?.Result is { } resultSelector)
            results = FromSelectors(document, store, baseUri, resultSelector);
        if (results.Count == 0)
            results = FromJsonLdItemList(document, store, baseUri);
        if (results.Count == 0)
            results = FromHeuristics(document, store, baseUri);
        return results;
    }

    private static List<Offer> FromSelectors(IDocument document, Store store, Uri baseUri, string resultSelector)
    {
        var selectors = store.Selectors!;
        var offers = new List<Offer>();
        foreach (var card in document.QuerySelectorAll(resultSelector))
        {
            var titleElement = selectors.Title is null ? null : card.QuerySelector(selectors.Title);
            var linkElement = selectors.Link is null ? card.QuerySelector("a[href]") : card.QuerySelector(selectors.Link);
            var title = Clean(titleElement?.TextContent ?? titleElement?.GetAttribute("title") ?? linkElement?.GetAttribute("title") ?? linkElement?.TextContent);
            var price = selectors.Price is null ? FindPriceInCard(card) : PriceExtractor.FromSelector(card, selectors.Price);
            var href = linkElement?.GetAttribute("href");

            if (price is null || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) continue;
            offers.Add(new Offer(store.Id, store.Name, title, price.Value, Absolute(baseUri, href), store.PickupMadrid));
        }
        return offers;
    }

    private static List<Offer> FromJsonLdItemList(IDocument document, Store store, Uri baseUri)
    {
        var offers = new List<Offer>();
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                CollectProducts(json.RootElement, store, baseUri, offers, 0);
            }
            catch (JsonException) { }
        }
        return offers;
    }

    private static void CollectProducts(JsonElement element, Store store, Uri baseUri, List<Offer> offers, int depth)
    {
        if (depth > 12) return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CollectProducts(child, store, baseUri, offers, depth + 1);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        var type = element.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (type == "Product" && element.TryGetProperty("name", out var name) && element.TryGetProperty("offers", out var offer))
        {
            var price = ExtractJsonPrice(offer);
            var url = element.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (url is null && offer.ValueKind == JsonValueKind.Object && offer.TryGetProperty("url", out var ou) && ou.ValueKind == JsonValueKind.String)
                url = ou.GetString();
            if (price is not null && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url))
                offers.Add(new Offer(store.Id, store.Name, Clean(name.GetString())!, price.Value, Absolute(baseUri, url), store.PickupMadrid));
        }

        foreach (var property in element.EnumerateObject())
            CollectProducts(property.Value, store, baseUri, offers, depth + 1);
    }

    private static decimal? ExtractJsonPrice(JsonElement offer)
    {
        if (offer.ValueKind == JsonValueKind.Array)
            return offer.EnumerateArray().Select(ExtractJsonPrice).FirstOrDefault(p => p is not null);
        if (offer.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "lowPrice", "price" })
        {
            if (!offer.TryGetProperty(key, out var value)) continue;
            var parsed = value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : PriceParser.Parse(value.GetString());
            if (parsed is > 0) return parsed;
        }
        return null;
    }

    /// <summary>
    /// Heurística genérica: cada enlace con texto cuya "tarjeta" (ancestro cercano) contenga un precio en euros.
    /// Funciona razonablemente en muchas tiendas sin configurar selectores.
    /// </summary>
    private static List<Offer> FromHeuristics(IDocument document, Store store, Uri baseUri)
    {
        var offers = new List<Offer>();
        var seen = new HashSet<string>();

        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase)) continue;

            var title = Clean(link.GetAttribute("title") ?? link.TextContent);
            if (title is null || title.Length < 8) continue;

            var card = FindCard(link);
            if (card is null) continue;
            var price = FindPriceInCard(card);
            if (price is null) continue;

            var absolute = Absolute(baseUri, href);
            if (!seen.Add(absolute)) continue;
            offers.Add(new Offer(store.Id, store.Name, title, price.Value, absolute, store.PickupMadrid));
        }
        return offers;
    }

    /// <summary>
    /// Sube por los ancestros del enlace hasta encontrar uno con precio. Si antes aparece un ancestro que
    /// agrupa varios productos (más de un enlace distinto al nuestro), no es una tarjeta sino una lista.
    /// </summary>
    private static IElement? FindCard(IElement link)
    {
        var href = link.GetAttribute("href");
        var current = link.ParentElement;
        for (var i = 0; i < 6 && current is not null; i++)
        {
            var otherLinks = current.QuerySelectorAll("a[href]")
                .Select(a => a.GetAttribute("href"))
                .Where(h => h is not null && h != href)
                .Distinct()
                .Count();
            if (otherLinks > 1) return null;
            if (current.TextContent.Contains('€')) return current;
            current = current.ParentElement;
        }
        return null;
    }

    private static decimal? FindPriceInCard(IElement card)
    {
        var fromClass = PriceExtractor.FromSelector(card, "[class*='price'], [data-price], [itemprop='price']");
        if (fromClass is not null) return fromClass;

        var text = card.TextContent;
        var euro = text.IndexOf('€');
        if (euro < 0) return null;
        var start = Math.Max(0, euro - 12);
        return PriceParser.Parse(text[start..Math.Min(text.Length, euro + 8)]);
    }

    private static string Absolute(Uri baseUri, string href) =>
        Uri.TryCreate(baseUri, href, out var absolute) ? absolute.ToString() : href;

    private static string? Clean(string? text)
    {
        if (text is null) return null;
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static Dictionary<string, string>? MergeHeaders(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (a is null && b is null) return null;
        var merged = new Dictionary<string, string>(a ?? new(), StringComparer.OrdinalIgnoreCase);
        if (b is not null) foreach (var (k, v) in b) merged[k] = v;
        return merged;
    }
}
