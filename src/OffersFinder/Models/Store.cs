using System.Text.Json.Serialization;

namespace OffersFinder.Models;

/// <summary>Una tienda del catálogo en la que se pueden buscar productos.</summary>
public sealed class Store
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Plantilla de URL de búsqueda; {query} se sustituye por el texto codificado.</summary>
    [JsonPropertyName("searchUrl")]
    public string SearchUrl { get; set; } = "";

    [JsonPropertyName("shipsToSpain")]
    public bool ShipsToSpain { get; set; } = true;

    /// <summary>Permite recoger el pedido en algún punto de la Comunidad de Madrid.</summary>
    [JsonPropertyName("pickupMadrid")]
    public bool PickupMadrid { get; set; }

    [JsonPropertyName("pickupNote")]
    public string? PickupNote { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Otros nombres con los que aparece la tienda en los agregadores (p. ej. "Amazon.es", "amazon").</summary>
    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    /// <summary>Comprueba si un nombre de tienda (tal como lo da Google Shopping) corresponde a esta tienda.</summary>
    public bool MatchesName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return false;
        var source = Normalize(sourceName);
        var candidates = new List<string> { Normalize(Name), Normalize(Id) };
        if (Aliases is not null) candidates.AddRange(Aliases.Select(Normalize));
        if (Uri.TryCreate(SearchUrl, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
            candidates.Add(Normalize(host));
            candidates.Add(Normalize(host.Split('.')[0]));
        }
        return candidates.Where(c => c.Length >= 3).Any(c => source == c || source.Contains(c) || c.Contains(source));
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Selectores CSS de la página de resultados. Si faltan, se usa la heurística genérica.</summary>
    [JsonPropertyName("selectors")]
    public StoreSelectors? Selectors { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    public string BuildSearchUrl(string query) =>
        SearchUrl.Replace("{query}", Uri.EscapeDataString(query));
}

public sealed class StoreSelectors
{
    /// <summary>Selector de cada tarjeta de resultado.</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>Selector del título, relativo a la tarjeta.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Selector del precio, relativo a la tarjeta.</summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>Selector del enlace (elemento &lt;a&gt;), relativo a la tarjeta.</summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

public sealed class StoreCatalog
{
    [JsonPropertyName("stores")]
    public List<Store> Stores { get; set; } = new();
}

/// <summary>Una oferta encontrada en una tienda.</summary>
public sealed record Offer(string StoreId, string StoreName, string Title, decimal Price, string Url, bool PickupMadrid);
