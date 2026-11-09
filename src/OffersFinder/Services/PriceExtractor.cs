using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace OffersFinder.Services;

public enum Availability { Unknown, InStock, OutOfStock }

/// <summary>Extrae el precio de la página de un producto.</summary>
public static class PriceExtractor
{
    private static readonly HtmlParser Parser = new();

    public static IDocument ParseDocument(string html) => Parser.ParseDocument(html);

    /// <summary>
    /// Orden de búsqueda: selector CSS del usuario, JSON-LD (schema.org Product/Offer),
    /// metaetiquetas de precio, atributos itemprop y, por último, elementos con clase "price".
    /// </summary>
    public static decimal? Extract(string html, string? selector = null)
    {
        var document = ParseDocument(html);

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var price = FromSelector(document, selector);
            if (price is not null) return price;
        }

        return FromJsonLd(document) ?? FromMetaTags(document) ?? FromItemProp(document) ?? FromPriceClasses(document);
    }

    /// <summary>Solo fuentes estructuradas (JSON-LD, metaetiquetas, itemprop): fiable para páginas desconocidas.</summary>
    public static decimal? ExtractStructured(IDocument document) =>
        FromJsonLd(document) ?? FromMetaTags(document) ?? FromItemProp(document);

    /// <summary>
    /// Disponibilidad declarada por la página: schema.org (InStock/OutOfStock...), metaetiquetas de producto o, si no,
    /// el texto de los elementos de stock ("agotado", "sin stock", "sold out").
    /// </summary>
    public static Availability ExtractAvailability(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var value = FindString(json.RootElement, "availability", 0);
                if (value is not null) return ClassifyAvailability(value);
            }
            catch (JsonException) { }
        }

        foreach (var selector in new[] { "meta[property='product:availability']", "meta[property='og:availability']", "[itemprop='availability']" })
        {
            var element = document.QuerySelector(selector);
            var value = element?.GetAttribute("content") ?? element?.GetAttribute("href") ?? element?.TextContent;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var classified = ClassifyAvailability(value);
                if (classified != Availability.Unknown) return classified;
            }
        }

        foreach (var element in document.QuerySelectorAll("[class*='stock'], [class*='availab'], [class*='disponib'], [id*='stock'], [id*='availab'], [id*='disponib']"))
        {
            var text = TextMatch.Normalize(element.TextContent);
            if (text.Length == 0 || text.Length > 120) continue;
            if (OutOfStockPhrases.Any(text.Contains)) return Availability.OutOfStock;
            if (InStockPhrases.Any(text.Contains)) return Availability.InStock;
        }
        return Availability.Unknown;
    }

    private static readonly string[] OutOfStockPhrases =
        { "agotado", "sin stock", "fuera de stock", "no disponible", "sold out", "out of stock", "esgotado", "descatalogado", "temporalmente sin" };
    private static readonly string[] InStockPhrases =
        { "en stock", "in stock", "disponible", "hay stock", "ultimas unidades", "envio inmediato" };

    private static Availability ClassifyAvailability(string value)
    {
        var v = TextMatch.Normalize(value);
        if (v.Contains("outofstock") || v.Contains("soldout") || v.Contains("discontinued") || v.Contains("out of stock") || v == "oos" || v.Contains("agotado"))
            return Availability.OutOfStock;
        if (v.Contains("instock") || v.Contains("in stock") || v.Contains("preorder") || v.Contains("limitedavailability") || v.Contains("onlineonly") || v.Contains("backorder") || v.Contains("instoreonly"))
            return Availability.InStock;
        return Availability.Unknown;
    }

    /// <summary>
    /// Comprueba que los elementos de precio de la página (clases price/precio, itemprop=price, metaetiquetas de precio)
    /// muestran euros. Una mención a "EUR" en un selector de monedas del pie no cuenta.
    /// </summary>
    public static bool PriceElementsMentionEuro(IDocument document)
    {
        foreach (var element in document.QuerySelectorAll("[class*='price'], [class*='precio'], [itemprop='price'], [id*='price'], [data-price]"))
        {
            var text = (element.GetAttribute("content") ?? "") + " " + element.TextContent;
            if (text.Length > 400) continue; // contenedores grandes, no un precio
            if (text.Contains('€') || text.Contains("EUR", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Moneda declarada por la página (JSON-LD priceCurrency, metaetiquetas, itemprop) o null si no la indica.</summary>
    public static string? ExtractCurrency(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var currency = FindString(json.RootElement, "priceCurrency", 0);
                if (currency is not null) return currency.ToUpperInvariant();
            }
            catch (JsonException) { }
        }
        foreach (var selector in new[] { "meta[property='product:price:currency']", "meta[property='og:price:currency']", "[itemprop='priceCurrency']" })
        {
            var element = document.QuerySelector(selector);
            var value = element?.GetAttribute("content") ?? element?.TextContent;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim().ToUpperInvariant();
        }
        return null;
    }

    private static string? FindString(JsonElement element, string property, int depth)
    {
        if (depth > 12) return null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
                foreach (var child in element.EnumerateObject())
                {
                    var nested = FindString(child.Value, property, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    var nested = FindString(child, property, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>Nombre del producto según la página: JSON-LD, og:title o &lt;title&gt;.</summary>
    public static string? ProductName(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var name = FindProductName(json.RootElement, 0);
                if (name is not null) return name;
            }
            catch (JsonException) { }
        }
        var og = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content");
        if (!string.IsNullOrWhiteSpace(og)) return og.Trim();
        return string.IsNullOrWhiteSpace(document.Title) ? null : document.Title.Trim();
    }

    private static string? FindProductName(JsonElement element, int depth)
    {
        if (depth > 12) return null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString() == "Product" && element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    return name.GetString();
                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindProductName(property.Value, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    var nested = FindProductName(child, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;
            default:
                return null;
        }
    }

    public static decimal? FromSelector(IParentNode root, string selector)
    {
        foreach (var element in root.QuerySelectorAll(selector))
        {
            var text = element.GetAttribute("content") ?? element.TextContent;
            var price = PriceParser.Parse(text);
            if (price is not null) return price;
        }
        return null;
    }

    public static decimal? FromJsonLd(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var price = FindPriceInJson(json.RootElement);
                if (price is not null) return price;
            }
            catch (JsonException)
            {
                // JSON-LD mal formado: seguimos con el siguiente bloque.
            }
        }
        return null;
    }

    private static decimal? FindPriceInJson(JsonElement element, int depth = 0)
    {
        if (depth > 12) return null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var key in new[] { "lowPrice", "price" })
                {
                    if (element.TryGetProperty(key, out var value))
                    {
                        var parsed = value.ValueKind switch
                        {
                            JsonValueKind.Number => value.GetDecimal(),
                            JsonValueKind.String => PriceParser.Parse(value.GetString()),
                            _ => null,
                        };
                        if (parsed is > 0) return parsed;
                    }
                }
                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindPriceInJson(property.Value, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    var nested = FindPriceInJson(child, depth + 1);
                    if (nested is not null) return nested;
                }
                return null;

            default:
                return null;
        }
    }

    public static decimal? FromMetaTags(IDocument document)
    {
        var selectors = new[]
        {
            "meta[property='product:price:amount']",
            "meta[property='og:price:amount']",
            "meta[name='twitter:data1']",
            "meta[itemprop='price']",
        };
        foreach (var selector in selectors)
        {
            var price = FromSelector(document, selector);
            if (price is not null) return price;
        }
        return null;
    }

    public static decimal? FromItemProp(IDocument document) =>
        FromSelector(document, "[itemprop='lowPrice'], [itemprop='price']");

    public static decimal? FromPriceClasses(IDocument document)
    {
        var selectors = new[]
        {
            "[class*='price-current']", "[class*='current-price']", "[class*='sale-price']", "[class*='price__value']",
            "[class*='product-price']", "[class*='price-now']", "[id*='priceblock']", "[class*='a-price-whole']",
            "[data-price]", "[class*='price']",
        };
        foreach (var selector in selectors)
        {
            foreach (var element in document.QuerySelectorAll(selector))
            {
                var text = element.GetAttribute("data-price") ?? element.GetAttribute("content") ?? element.TextContent;
                var price = PriceParser.Parse(text);
                if (price is not null) return price;
            }
        }
        return null;
    }
}
