using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Resultado de leer una página encontrada en Google: qué pasó y, si procede, la oferta.</summary>
public sealed record ScannedPage(string Url, string Outcome, Offer? Offer);

/// <summary>
/// Lee las páginas que devuelve la búsqueda web de Google y extrae el precio cuando la página
/// lo declara de forma estructurada (JSON-LD, metaetiquetas, itemprop). Las tiendas que bloquean
/// el acceso automático se omiten.
/// </summary>
public sealed class WebResultScanner
{
    private static readonly string[] SkippedHosts =
    {
        "google.", "youtube.", "wikipedia.", "facebook.", "instagram.", "twitter.", "x.com", "reddit.", "tiktok.",
        "pinterest.", "forocoches.", "amazon.", "idealo.", "wallapop.", "milanuncios.", "ebay.", "aliexpress.",
    };

    private readonly PageFetcher _fetcher;
    private readonly IReadOnlyList<Store> _stores;
    private readonly IReadOnlyCollection<string> _globalBlacklist;
    private readonly int _maxPages;

    public WebResultScanner(PageFetcher fetcher, IReadOnlyList<Store> stores, IReadOnlyCollection<string>? globalBlacklist = null, int maxPages = 10)
    {
        _fetcher = fetcher;
        _stores = stores;
        _globalBlacklist = globalBlacklist ?? Array.Empty<string>();
        _maxPages = maxPages;
    }

    public async Task<List<ScannedPage>> ScanAsync(WatchItem item, IEnumerable<WebResult> results, CancellationToken ct = default)
    {
        var scanned = new List<ScannedPage>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesRead = 0;

        foreach (var result in results)
        {
            if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)) continue;
            var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;

            if (SkippedHosts.Any(h => host.Contains(h, StringComparison.OrdinalIgnoreCase) || host.Equals(h.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)))
            {
                scanned.Add(new ScannedPage(result.Url, "omitida (no es una tienda o bloquea el acceso)", null));
                continue;
            }

            var store = _stores.FirstOrDefault(s => Uri.TryCreate(s.SearchUrl, UriKind.Absolute, out var su) &&
                                                   string.Equals(su.Host.StartsWith("www.") ? su.Host[4..] : su.Host, host, StringComparison.OrdinalIgnoreCase));
            var storeId = store?.Id ?? Store.Normalize(host.Split('.')[0]);
            var storeName = store?.Name ?? host;

            if ((store is not null && !store.Enabled) ||
                _globalBlacklist.Contains(storeId, StringComparer.OrdinalIgnoreCase) ||
                (item.ExcludeStores?.Contains(storeId, StringComparer.OrdinalIgnoreCase) ?? false))
            {
                scanned.Add(new ScannedPage(result.Url, "tienda en lista negra", null));
                continue;
            }
            if (item.Stores is { Count: > 0 } && !item.Stores.Contains(storeId, StringComparer.OrdinalIgnoreCase))
            {
                scanned.Add(new ScannedPage(result.Url, "tienda fuera de la lista del producto", null));
                continue;
            }
            var pickup = store?.PickupMadrid ?? false;
            if (item.PickupMadridOnly && !pickup)
            {
                scanned.Add(new ScannedPage(result.Url, "sin recogida en Madrid", null));
                continue;
            }
            if (!seenHosts.Add(host) && pagesRead >= _maxPages / 2)
            {
                scanned.Add(new ScannedPage(result.Url, "omitida (misma tienda ya leída)", null));
                continue;
            }
            if (pagesRead >= _maxPages)
            {
                scanned.Add(new ScannedPage(result.Url, "omitida (límite de páginas)", null));
                continue;
            }

            pagesRead++;
            string html;
            try
            {
                html = await _fetcher.GetHtmlAsync(result.Url, item.Headers, ct);
            }
            catch (FetchException ex)
            {
                scanned.Add(new ScannedPage(result.Url, ex.Message.Contains("403") ? "la tienda bloquea el acceso automático" : ex.Message, null));
                continue;
            }

            var document = PriceExtractor.ParseDocument(html);
            // Solo tiendas que venden en euros: una página en otra moneda es casi seguro de otro país.
            var currency = PriceExtractor.ExtractCurrency(document);
            if (currency is not null && currency != "EUR")
            {
                scanned.Add(new ScannedPage(result.Url, $"precio en otra moneda ({currency}), no vende en España", null));
                continue;
            }
            if (currency is null && !html.Contains('€') && !html.Contains("EUR", StringComparison.Ordinal))
            {
                scanned.Add(new ScannedPage(result.Url, "la página no muestra precios en euros", null));
                continue;
            }
            var title = PriceExtractor.ProductName(document) ?? result.Title;
            if (PriceExtractor.ExtractAvailability(document) == Availability.OutOfStock)
            {
                scanned.Add(new ScannedPage(result.Url, $"agotado: \"{Truncate(title, 60)}\"", null));
                continue;
            }
            var price = PriceExtractor.ExtractStructured(document);
            var viaCss = false;
            if (price is null && TitleMatches(item, title))
            {
                // Sin datos estructurados, pero el título de la página es el producto buscado:
                // aceptamos el precio marcado con clases CSS (menos fiable, se indica en el aviso).
                price = PriceExtractor.FromPriceClasses(document);
                viaCss = price is not null;
            }
            if (price is null)
            {
                scanned.Add(new ScannedPage(result.Url, "sin precio estructurado en la página", null));
                continue;
            }
            var offer = new Offer(storeId, storeName, viaCss ? title + " (precio leído del texto)" : title, price.Value, result.Url, pickup);
            if (!OfferSearcher.Matches(item, offer))
            {
                scanned.Add(new ScannedPage(result.Url, $"no encaja: {PriceParser.Format(price.Value, item.Currency)} \"{Truncate(title, 60)}\"", null));
                continue;
            }
            scanned.Add(new ScannedPage(result.Url, "✓", offer));
        }
        return scanned;
    }

    /// <summary>El título de la página contiene todas las palabras requeridas del producto.</summary>
    public static bool TitleMatches(WatchItem item, string title) =>
        TextMatch.ContainsAll(title, TextMatch.RequiredTerms(item));

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
