using System.Text;
using System.Text.Json;
using OffersFinder.Models;

namespace OffersFinder.Services;

public enum ShoppingProvider { Serper, SerpApi }

/// <summary>
/// Obtiene ofertas de los resultados de Google Shopping para España a través de un proveedor de API
/// (Serper.dev o SerpApi). Una sola consulta devuelve precios de la mayoría de tiendas españolas.
/// </summary>
public sealed class ShoppingSearchClient
{
    private readonly string _apiKey;
    private readonly ShoppingProvider _provider;
    private readonly string _endpoint;
    private readonly string _location;
    private readonly HttpClient _http;

    public ShoppingSearchClient(string apiKey, HttpClient? http = null, string? endpoint = null, string location = "Spain",
        ShoppingProvider provider = ShoppingProvider.Serper)
    {
        _apiKey = apiKey;
        _provider = provider;
        _endpoint = endpoint ?? (provider == ShoppingProvider.SerpApi ? "https://serpapi.com/search.json" : "https://google.serper.dev/shopping");
        _location = location;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string ProviderName => _provider == ShoppingProvider.SerpApi ? "SerpApi" : "Serper";

    public async Task<List<ShoppingResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        using var request = BuildRequest(query);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new FetchException($"{ProviderName} respondió {(int)response.StatusCode}: {Truncate(json)}");
        return Parse(json);
    }

    /// <summary>Búsqueda web normal en Google (resultados orgánicos): páginas de tiendas que no aparecen en Shopping.</summary>
    public async Task<List<WebResult>> WebSearchAsync(string query, CancellationToken ct = default)
    {
        using var request = BuildRequest(query, web: true);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new FetchException($"{ProviderName} respondió {(int)response.StatusCode}: {Truncate(json)}");
        return ParseWeb(json);
    }

    private HttpRequestMessage BuildRequest(string query, bool web = false)
    {
        if (_provider == ShoppingProvider.SerpApi)
        {
            var endpoint = _endpoint;
            var engine = web ? "google" : "google_shopping";
            var url = $"{endpoint}?engine={engine}&q={Uri.EscapeDataString(query)}&gl=es&hl=es&num=20" +
                      $"&location={Uri.EscapeDataString(_location)}&api_key={Uri.EscapeDataString(_apiKey)}";
            return new HttpRequestMessage(HttpMethod.Get, url);
        }

        var serperEndpoint = web ? _endpoint.Replace("/shopping", "/search") : _endpoint;
        var payload = JsonSerializer.Serialize(new { q = query, gl = "es", hl = "es", location = _location, num = web ? 20 : 40 });
        var request = new HttpRequestMessage(HttpMethod.Post, serperEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-API-KEY", _apiKey);
        return request;
    }

    /// <summary>Entiende la respuesta de Serper ("organic") y la de SerpApi ("organic_results").</summary>
    public static List<WebResult> ParseWeb(string json)
    {
        var results = new List<WebResult>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            throw new FetchException($"El proveedor devolvió un error: {error.GetString()}");

        JsonElement list = default;
        var found = (doc.RootElement.TryGetProperty("organic", out list) || doc.RootElement.TryGetProperty("organic_results", out list))
                    && list.ValueKind == JsonValueKind.Array;
        if (!found) return results;

        foreach (var item in list.EnumerateArray())
        {
            var title = Str(item, "title");
            var link = Str(item, "link");
            if (title is null || link is null || !link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            results.Add(new WebResult(title, link, Str(item, "snippet")));
        }
        return results;
    }

    /// <summary>Entiende tanto la respuesta de Serper ("shopping") como la de SerpApi ("shopping_results").</summary>
    public static List<ShoppingResult> Parse(string json)
    {
        var results = new List<ShoppingResult>();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            throw new FetchException($"El proveedor devolvió un error: {error.GetString()}");

        JsonElement list = default;
        var found = (doc.RootElement.TryGetProperty("shopping", out list) || doc.RootElement.TryGetProperty("shopping_results", out list))
                    && list.ValueKind == JsonValueKind.Array;
        if (!found) return results;

        foreach (var item in list.EnumerateArray())
        {
            var title = Str(item, "title");
            var link = Str(item, "link") ?? Str(item, "product_link");
            var source = Str(item, "source");
            decimal? price = item.TryGetProperty("extracted_price", out var extracted) && extracted.ValueKind == JsonValueKind.Number
                ? extracted.GetDecimal()
                : PriceParser.Parse(Str(item, "price"));
            if (title is null || link is null || price is null or <= 0) continue;
            results.Add(new ShoppingResult(title, price.Value, link, source ?? HostOf(link), Str(item, "delivery")));
        }
        return results;
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "desconocida";
    private static string Truncate(string text) => text.Length > 200 ? text[..200] + "…" : text;
}

public sealed record ShoppingResult(string Title, decimal Price, string Url, string Source, string? Delivery);
public sealed record WebResult(string Title, string Url, string? Snippet);
