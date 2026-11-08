using System.Net;
using OffersFinder.Models;

namespace OffersFinder.Services;

public sealed class FetchException : Exception
{
    public FetchException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Descarga páginas HTML con reintentos y cabeceras de navegador.</summary>
public sealed class PageFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _retries;

    public PageFetcher(Settings settings, int retries = 3, HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "es-ES,es;q=0.9,en;q=0.8");
        _retries = retries;
        _debugDir = settings.DebugDir;
    }

    private readonly string? _debugDir;

    /// <summary>Si DEBUG_DIR está definido, guarda la respuesta de cada página para poder ajustar los selectores.</summary>
    private void Dump(string url, int status, string body)
    {
        if (_debugDir is null) return;
        try
        {
            Directory.CreateDirectory(_debugDir);
            var safe = new string(url.Replace("https://", "").Replace("http://", "").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            if (safe.Length > 120) safe = safe[..120];
            File.WriteAllText(Path.Combine(_debugDir, $"{status}_{safe}.html"), body);
        }
        catch (IOException) { }
    }

    public async Task<string> GetHtmlAsync(string url, IReadOnlyDictionary<string, string>? extraHeaders = null, CancellationToken ct = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _retries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (extraHeaders is not null)
                    foreach (var (key, value) in extraHeaders)
                        request.Headers.TryAddWithoutValidation(key, value);

                using var response = await _http.SendAsync(request, ct);
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct);
                Dump(url, status, body);
                if (status == 429 || status >= 500)
                    throw new FetchException($"HTTP {status}{Protection(body, response)}");
                if (status == 403)
                    throw new FetchException($"HTTP 403: la tienda no permite el acceso automático{Protection(body, response)}");
                if (status >= 400)
                    throw new FetchException($"HTTP {status}");

                return body;
            }
            catch (Exception ex) when (ex is HttpRequestException or FetchException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                lastError = ex;
                if (attempt < _retries)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        throw new FetchException($"No se pudo descargar {url}: {lastError?.Message}", lastError);
    }

    /// <summary>Identifica, a título informativo, el sistema de protección que ha rechazado la petición.</summary>
    private static string Protection(string body, HttpResponseMessage response)
    {
        var server = response.Headers.TryGetValues("Server", out var values) ? string.Join(",", values) : "";
        if (server.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) || body.Contains("cf-chl", StringComparison.OrdinalIgnoreCase))
            return " (protección Cloudflare)";
        if (server.Contains("AkamaiGHost", StringComparison.OrdinalIgnoreCase) || body.Contains("_abck", StringComparison.Ordinal))
            return " (protección Akamai)";
        if (body.Contains("datadome", StringComparison.OrdinalIgnoreCase))
            return " (protección DataDome)";
        if (body.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            return " (captcha)";
        return "";
    }

    public void Dispose() => _http.Dispose();
}
