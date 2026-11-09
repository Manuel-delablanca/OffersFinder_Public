using System.Text.Json.Serialization;

namespace OffersFinder.Models;

/// <summary>Un producto que queremos vigilar. Puede ser una URL concreta, una búsqueda por texto, o ambas.</summary>
public sealed class WatchItem
{
    /// <summary>Nombre descriptivo (solo para mostrarlo en los avisos).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>URL concreta de la página del producto (opcional).</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Otras fichas de producto a leer directamente (sin gastar consultas de la API).</summary>
    [JsonPropertyName("urls")]
    public List<string>? Urls { get; set; }

    /// <summary>Texto a buscar en el catálogo de tiendas (opcional).</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    /// <summary>Palabras que deben aparecer en el título del resultado (si se omite, se usan las de la búsqueda).</summary>
    [JsonPropertyName("mustContain")]
    public List<string>? MustContain { get; set; }

    /// <summary>Palabras que descartan un resultado (fundas, accesorios, reacondicionados...).</summary>
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }

    /// <summary>Limitar la búsqueda a estos identificadores de tienda del catálogo.</summary>
    [JsonPropertyName("stores")]
    public List<string>? Stores { get; set; }

    /// <summary>Tiendas (identificadores del catálogo) que no se consultarán para este producto.</summary>
    [JsonPropertyName("excludeStores")]
    public List<string>? ExcludeStores { get; set; }

    /// <summary>Si es true, solo se consideran tiendas con recogida en la Comunidad de Madrid.</summary>
    [JsonPropertyName("pickupMadridOnly")]
    public bool PickupMadridOnly { get; set; }

    /// <summary>Si es true, el precio que se compara (bajadas, objetivo, orden) incluye el envío cuando se conoce.</summary>
    [JsonPropertyName("compareWithShipping")]
    public bool CompareWithShipping { get; set; }

    /// <summary>Precio objetivo: se avisa cuando el precio sea igual o inferior.</summary>
    [JsonPropertyName("targetPrice")]
    public decimal? TargetPrice { get; set; }

    /// <summary>Precio máximo plausible: los resultados por encima se ignoran (evita packs o productos distintos).</summary>
    [JsonPropertyName("maxPrice")]
    public decimal? MaxPrice { get; set; }

    /// <summary>Precio mínimo plausible: los resultados por debajo se ignoran (evita accesorios baratos).</summary>
    [JsonPropertyName("minPrice")]
    public decimal? MinPrice { get; set; }

    /// <summary>Selector CSS opcional del elemento que contiene el precio (solo para <see cref="Url"/>).</summary>
    [JsonPropertyName("selector")]
    public string? Selector { get; set; }

    /// <summary>Avisar ante cualquier bajada respecto al último precio registrado.</summary>
    [JsonPropertyName("alertOnDrop")]
    public bool AlertOnDrop { get; set; } = true;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "€";

    /// <summary>Cabeceras HTTP adicionales (por ejemplo cookies) para tiendas exigentes.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonIgnore]
    public string Key => !string.IsNullOrWhiteSpace(Url) ? Url! : $"query:{Query?.Trim().ToLowerInvariant()}";

    [JsonIgnore]
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    /// <summary>Todas las URL del producto: la principal y las adicionales.</summary>
    [JsonIgnore]
    public IEnumerable<string> AllUrls =>
        (HasUrl ? new[] { Url! } : Array.Empty<string>()).Concat(Urls ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct();

    [JsonIgnore]
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (Url ?? Query ?? "?") : Name;
}

public sealed class Watchlist
{
    /// <summary>Lista negra global: estas tiendas no se consultan para ningún producto.</summary>
    [JsonPropertyName("blacklistedStores")]
    public List<string> BlacklistedStores { get; set; } = new();

    [JsonPropertyName("items")]
    public List<WatchItem> Items { get; set; } = new();

    public bool IsBlacklisted(string storeId, WatchItem? item = null) =>
        BlacklistedStores.Contains(storeId, StringComparer.OrdinalIgnoreCase) ||
        (item?.ExcludeStores?.Contains(storeId, StringComparer.OrdinalIgnoreCase) ?? false);
}
