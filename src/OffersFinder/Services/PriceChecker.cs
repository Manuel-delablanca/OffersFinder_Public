using System.Globalization;
using OffersFinder.Models;
using OffersFinder.Notifications;

namespace OffersFinder.Services;

/// <summary>Resultado de comprobar un producto.</summary>
public sealed record CheckResult(WatchItem Item, decimal? Price, PriceRecord? Previous, List<Alert> Alerts, string? Error, List<Offer> Offers);

/// <summary>Orquesta la comprobación: obtiene precios, los compara con el histórico y genera avisos.</summary>
public sealed class PriceChecker
{
    private readonly Settings _settings;
    private readonly PageFetcher _fetcher;
    private readonly OfferSearcher _searcher;
    private readonly Action<string> _log;

    public PriceChecker(Settings settings, PageFetcher fetcher, OfferSearcher searcher, Action<string>? log = null)
    {
        _settings = settings;
        _fetcher = fetcher;
        _searcher = searcher;
        _log = log ?? Console.WriteLine;
    }

    public async Task<CheckResult> CheckAsync(WatchItem item, Dictionary<string, ItemHistory> history, DateTimeOffset now, CancellationToken ct = default)
    {
        var offers = new List<Offer>();
        decimal? price = null;
        string? error = null;
        string? bestUrl = null;
        string? bestStore = null;
        string? bestTitle = null;

        // 1) Fichas de producto concretas (no gastan consultas de la API)
        foreach (var url in item.AllUrls)
        {
            var urlStore = _searcher.StoreForUrl(url);
            if (urlStore is not null && _searcher.IsBlacklisted(urlStore, item))
            {
                _log($"  · {urlStore.Name} está en la lista negra: se omite {url}");
                continue;
            }
            try
            {
                var html = await _fetcher.GetHtmlAsync(url, item.Headers, ct);
                var document = PriceExtractor.ParseDocument(html);
                if (PriceExtractor.ExtractAvailability(document) == Availability.OutOfStock)
                {
                    _log($"  · {HostOf(url)}: agotado, se omite");
                    continue;
                }
                var found = !string.IsNullOrWhiteSpace(item.Selector) ? PriceExtractor.FromSelector(document, item.Selector) : null;
                found ??= PriceExtractor.ExtractStructured(document) ?? PriceExtractor.FromPriceClasses(document);
                if (found is null)
                {
                    _log($"  · {HostOf(url)}: no se encontró ningún precio en la página (prueba a indicar 'selector')");
                    error ??= "No se encontró ningún precio en la página.";
                    continue;
                }
                var host = HostOf(url);
                var offer = new Offer(urlStore?.Id ?? Store.Normalize(host.Split('.')[0]), urlStore?.Name ?? host,
                    PriceExtractor.ProductName(document) ?? item.DisplayName, found.Value, url, urlStore?.PickupMadrid ?? false);
                offers.Add(offer);
                _log($"  · {host}: {PriceParser.Format(found.Value, item.Currency)}");
            }
            catch (FetchException ex)
            {
                _log($"  · {HostOf(url)}: {ex.Message}");
                error ??= ex.Message;
            }
        }

        // 2) Búsqueda (Google Shopping / web / tiendas), salvo que la API esté en pausa
        if (item.HasQuery && !_settings.ApiPaused)
            offers.AddRange(await _searcher.SearchAsync(item, ct));
        else if (item.HasQuery && item.AllUrls.Any() == false)
            error ??= $"API en pausa hasta {_settings.ApiPauseUntil:yyyy-MM-dd} y el producto no tiene URL que leer.";

        offers = offers.GroupBy(o => o.Url).Select(g => g.OrderBy(o => o.EffectivePrice(item)).First()).OrderBy(o => o.EffectivePrice(item)).ToList();
        var best = offers.FirstOrDefault();
        if (best is not null)
        {
            price = best.EffectivePrice(item);
            bestUrl = best.Url;
            bestStore = best.StoreName;
            bestTitle = best.Title;
            error = null;
        }
        else if (error is null && item.HasQuery)
            error = "Ninguna tienda devolvió resultados que encajen.";

        if (!history.TryGetValue(item.Key, out var itemHistory))
            history[item.Key] = itemHistory = new ItemHistory();
        itemHistory.Name = item.DisplayName;
        if (offers.Count > 0)
        {
            itemHistory.StoreRecords ??= new List<StoreRecord>();
            foreach (var group in offers.GroupBy(o => o.StoreName))
            {
                var cheapest = group.OrderBy(o => o.EffectivePrice(item)).First();
                itemHistory.StoreRecords.Add(new StoreRecord { Timestamp = now, Store = cheapest.StoreName, Price = cheapest.EffectivePrice(item), Url = cheapest.Url, Shipping = cheapest.Shipping, ShippingCost = cheapest.ShippingCost });
            }
            if (itemHistory.StoreRecords.Count > 20000)
                itemHistory.StoreRecords.RemoveRange(0, itemHistory.StoreRecords.Count - 20000);
        }
        if (offers.Count > 0 || item.HasQuery)
        {
            itemHistory.LastOffers = offers.Take(10).Select(o => new OfferRecord
            {
                Store = o.StoreName, Price = o.Price, Title = o.Title, Url = o.Url, PickupMadrid = o.PickupMadrid, Shipping = o.Shipping, ShippingCost = o.ShippingCost,
            }).ToList();
            itemHistory.LastOffersAt = now;
        }

        var previous = itemHistory.Last;
        var alerts = new List<Alert>();

        if (price is null)
            return new CheckResult(item, null, previous, alerts, error ?? "Sin precio", offers);

        var lowestBefore = itemHistory.LowestEver;
        alerts.AddRange(Evaluate(item, itemHistory, price.Value, previous, lowestBefore, offers));

        itemHistory.Records.Add(new PriceRecord
        {
            Timestamp = now,
            Price = price.Value,
            Store = bestStore,
            Url = bestUrl,
            Title = bestTitle,
        });

        return new CheckResult(item, price, previous, alerts, null, offers);
    }

    /// <summary>Reglas de aviso: bajada respecto al último precio y precio objetivo alcanzado.</summary>
    public IEnumerable<Alert> Evaluate(WatchItem item, ItemHistory itemHistory, decimal price, PriceRecord? previous, decimal? lowestBefore, List<Offer> offers)
    {
        var formatted = PriceParser.Format(price, item.Currency) + (item.CompareWithShipping ? " (incl. envío)" : "");
        // Si hay ofertas, sus enlaces ya van en el cuerpo del aviso; si no, enlazamos la URL del producto.
        var bestUrl = offers.Count > 0 ? null : item.Url;

        if (item.AlertOnDrop && previous is not null && price < previous.Price)
        {
            var dropPercent = (previous.Price - price) / previous.Price * 100m;
            if (dropPercent >= _settings.MinDropPercent)
            {
                var body = $"{item.DisplayName}\nAntes: {PriceParser.Format(previous.Price, item.Currency)} → Ahora: {formatted} " +
                           $"(-{dropPercent.ToString("0.#", CultureInfo.InvariantCulture)} %)";
                if (lowestBefore is not null && price < lowestBefore) body += "\n🏆 ¡Precio más bajo registrado!";
                body += OffersSummary(offers, item.Currency);
                yield return new Alert($"⬇️ Bajada de precio: {item.DisplayName}", body, bestUrl);
            }
        }

        if (item.TargetPrice is { } target && price <= target)
        {
            var alreadyAlerted = itemHistory.LastTargetAlertPrice is { } last && price >= last;
            if (!alreadyAlerted)
            {
                itemHistory.LastTargetAlertPrice = price;
                var body = $"{item.DisplayName}\nPrecio actual: {formatted} (objetivo: {PriceParser.Format(target, item.Currency)})";
                body += OffersSummary(offers, item.Currency);
                yield return new Alert($"🎯 Precio objetivo alcanzado: {item.DisplayName}", body, bestUrl, Pin: true);
            }
        }
        else if (item.TargetPrice is not null)
        {
            // Volvió a subir por encima del objetivo: la próxima vez que baje, avisamos de nuevo.
            itemHistory.LastTargetAlertPrice = null;
        }
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host) : url;

    private string OffersSummary(List<Offer> offers, string currency)
    {
        if (offers.Count == 0) return "";
        var lines = offers.Take(_settings.MaxOffersInAlert).Select(o =>
            $"• {o.StoreName}{(o.PickupMadrid ? " (recogida en Madrid)" : "")}: {PriceParser.Format(o.Price, currency)} · {o.ShippingLabel} – {Truncate(o.Title, 60)}\n  {o.Url}");
        return "\n\nMejores ofertas:\n" + string.Join("\n", lines);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
