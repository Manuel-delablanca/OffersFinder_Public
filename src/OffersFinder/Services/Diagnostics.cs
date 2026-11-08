using AngleSharp.Dom;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Imprime qué ve el programa en la página de resultados de cada tienda, para ajustar selectores.</summary>
public sealed class Diagnostics
{
    private readonly PageFetcher _fetcher;
    private readonly OfferSearcher _searcher;
    private readonly Action<string> _log;

    private readonly ShoppingSearchClient? _shopping;
    private readonly bool _useStores;

    private readonly bool _webSearch;
    private readonly bool _apiPaused;

    public Diagnostics(PageFetcher fetcher, OfferSearcher searcher, Action<string> log, ShoppingSearchClient? shopping = null, bool useStores = true, bool webSearch = true, bool apiPaused = false)
    {
        _apiPaused = apiPaused;
        _fetcher = fetcher;
        _searcher = searcher;
        _log = log;
        _shopping = shopping;
        _useStores = useStores;
        _webSearch = webSearch;
    }

    public async Task RunAsync(WatchItem item, CancellationToken ct = default)
    {
        _log($"=== {item.DisplayName} ===");

        foreach (var url in item.AllUrls)
        {
            _log($"URL: {url}");
            try
            {
                var html = await _fetcher.GetHtmlAsync(url, item.Headers, ct);
                var doc = PriceExtractor.ParseDocument(html);
                _log($"  HTTP OK, {html.Length} bytes, título: \"{Trim(doc.Title, 80)}\"");
                _log($"  selector: {Fmt(item.Selector is null ? null : PriceExtractor.FromSelector(doc, item.Selector))}  " +
                     $"json-ld: {Fmt(PriceExtractor.FromJsonLd(doc))}  meta: {Fmt(PriceExtractor.FromMetaTags(doc))}  " +
                     $"itemprop: {Fmt(PriceExtractor.FromItemProp(doc))}  clases price: {Fmt(PriceExtractor.FromPriceClasses(doc))}");
            }
            catch (FetchException ex)
            {
                _log($"  {ex.Message}");
            }
        }

        if (!item.HasQuery) return;
        if (_apiPaused)
        {
            _log("  (API de búsqueda en pausa: no se consulta Google)");
            return;
        }

        if (_shopping is not null)
        {
            _log($"[google-shopping] \"{item.Query}\" (gl=es)");
            try
            {
                var results = await _shopping.SearchAsync(item.Query!, ct);
                var offers = _searcher.OffersFromShopping(item, results);
                _log($"  {results.Count} resultados, {offers.Count} encajan con los filtros");
                // Primero las que encajan (todas) y después hasta 12 descartadas, para ver el motivo.
                var ordered = results.Where(r => offers.Any(o => o.Url == r.Url))
                    .Concat(results.Where(r => offers.All(o => o.Url != r.Url)).Take(12));
                foreach (var r in ordered)
                {
                    var store = _searcher.StoreForName(r.Source);
                    var offer = new Offer(store?.Id ?? Store.Normalize(r.Source), store?.Name ?? r.Source, r.Title, r.Price, r.Url, store?.PickupMadrid ?? false);
                    var status = offers.Any(o => o.Url == offer.Url) ? "✓" : "✗ " + (OfferSearcher.Matches(item, offer) ? "tienda excluida o sin recogida en Madrid" : WhyNot(item, offer));
                    _log($"    {status} {PriceParser.Format(r.Price, item.Currency),12}  {Trim(r.Source, 22),-22} {Trim(r.Title, 60)}");
                }
            }
            catch (FetchException ex)
            {
                _log($"  {ex.Message}");
            }
        }

        if (_shopping is not null && _webSearch)
        {
            _log($"[google-web] \"{item.Query}\" (gl=es)");
            try
            {
                var pages = await _searcher.ScanWebAsync(item, ct);
                _log($"  {pages.Count} resultados, {pages.Count(p => p.Offer is not null)} con precio que encaja");
                foreach (var page in pages)
                {
                    var text = page.Offer is null
                        ? $"✗ {page.Outcome}"
                        : $"✓ {PriceParser.Format(page.Offer.Price, item.Currency)}  {page.Offer.StoreName}  {Trim(page.Offer.Title, 50)}";
                    _log($"    {text}  ← {Trim(page.Url, 70)}");
                }
            }
            catch (FetchException ex)
            {
                _log($"  {ex.Message}");
            }
        }

        if (!_useStores) return;

        foreach (var store in _searcher.StoresFor(item))
        {
            var url = store.BuildSearchUrl(item.Query!);
            _log($"[{store.Id}] {url}");
            string html;
            try
            {
                html = await _fetcher.GetHtmlAsync(url, store.Headers, ct);
            }
            catch (FetchException ex)
            {
                _log($"  {ex.Message}");
                continue;
            }

            var doc = PriceExtractor.ParseDocument(html);
            _log($"  HTTP OK, {html.Length} bytes, título: \"{Trim(doc.Title, 80)}\", json-ld: {doc.QuerySelectorAll("script[type='application/ld+json']").Length} bloque(s)");

            if (store.Selectors?.Result is { } resultSelector)
            {
                var cards = doc.QuerySelectorAll(resultSelector);
                _log($"  selector de tarjeta \"{resultSelector}\": {cards.Length} coincidencias");
                foreach (var card in cards.Take(3))
                {
                    var title = store.Selectors.Title is null ? null : card.QuerySelector(store.Selectors.Title)?.TextContent;
                    var price = store.Selectors.Price is null ? null : card.QuerySelector(store.Selectors.Price)?.TextContent;
                    var link = (store.Selectors.Link is null ? card.QuerySelector("a[href]") : card.QuerySelector(store.Selectors.Link))?.GetAttribute("href");
                    _log($"    · título: \"{Trim(Collapse(title), 70)}\" | precio: \"{Trim(Collapse(price), 25)}\" | enlace: {Trim(link, 60)}");
                }
            }

            var offers = OfferSearcher.ParseResults(html, store, url);
            var matching = offers.Where(o => OfferSearcher.Matches(item, o)).ToList();
            _log($"  ofertas extraídas: {offers.Count}, encajan con los filtros: {matching.Count}");
            foreach (var offer in offers.Take(5))
            {
                var why = OfferSearcher.Matches(item, offer) ? "✓" : "✗ " + WhyNot(item, offer);
                _log($"    {why} {PriceParser.Format(offer.Price, item.Currency),12}  {Trim(offer.Title, 70)}");
            }
        }
    }

    private static string WhyNot(WatchItem item, Offer offer)
    {
        var title = offer.Title.ToLowerInvariant();
        var required = (item.MustContain is { Count: > 0 } ? item.MustContain : item.Query!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList())
            .Select(w => w.ToLowerInvariant()).ToList();
        var missing = required.Where(w => !title.Contains(w)).ToList();
        if (missing.Count > 0) return $"falta \"{string.Join("\", \"", missing)}\"";
        var excluded = item.Exclude?.FirstOrDefault(w => title.Contains(w.ToLowerInvariant()));
        if (excluded is not null) return $"excluido por \"{excluded}\"";
        if (item.MaxPrice is { } max && offer.Price > max) return $"supera maxPrice {max}";
        if (item.MinPrice is { } min && offer.Price < min) return $"por debajo de minPrice {min}";
        return "?";
    }

    private static string Fmt(decimal? price) => price is null ? "-" : PriceParser.Format(price.Value);
    private static string? Collapse(string? text) => text is null ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Trim(string? text, int max) => text is null ? "" : text.Length <= max ? text : text[..(max - 1)] + "…";
}
