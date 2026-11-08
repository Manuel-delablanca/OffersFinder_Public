namespace OffersFinder.Models;

/// <summary>Ajustes globales, leídos de variables de entorno (y de un archivo .env si existe).</summary>
public sealed class Settings
{
    public string WatchlistPath { get; init; } = "watchlist.json";
    public string StoresPath { get; init; } = "stores.json";
    public string HistoryPath { get; init; } = "data/history.json";
    public string ReportPath { get; init; } = "data/report.html";
    public decimal MinDropPercent { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 20;
    public int MaxOffersInAlert { get; init; } = 5;
    /// <summary>Carpeta donde volcar el HTML de cada página descargada (diagnóstico). Vacío = desactivado.</summary>
    public string? DebugDir { get; init; }

    /// <summary>Clave de Serper.dev. Si está definida, las búsquedas usan Google Shopping (España) en lugar de consultar cada tienda.</summary>
    public string? SerperApiKey { get; init; }
    /// <summary>Clave de SerpApi (serpapi.com), alternativa a Serper con los mismos resultados.</summary>
    public string? SerpApiKey { get; init; }
    public string SerperLocation { get; init; } = "Spain";
    /// <summary>Solo para pruebas: permite apuntar a un servidor que simule la API de Serper.</summary>
    public string? SerperEndpoint { get; init; }
    /// <summary>Además de Shopping, buscar en Google (web) y leer las páginas de resultados. Cuesta una consulta más por producto.</summary>
    public bool WebSearch { get; init; } = true;
    public int WebSearchMaxPages { get; init; } = 10;

    /// <summary>"shopping" (solo Google Shopping), "stores" (solo tiendas del catálogo) o "both".</summary>
    public string SearchMode { get; init; } = "auto";

    /// <summary>Hasta esta fecha (AAAA-MM-DD, inclusive el día anterior) no se hacen llamadas a la API de búsqueda; solo se leen las URL.</summary>
    public DateOnly? ApiPauseUntil { get; init; }
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.Now);
    public bool ApiPaused => ApiPauseUntil is { } until && Today < until;

    public bool HasShoppingKey => !string.IsNullOrWhiteSpace(SerperApiKey) || !string.IsNullOrWhiteSpace(SerpApiKey);
    public bool UseShoppingSearch => HasShoppingKey && !ApiPaused && SearchMode is "auto" or "shopping" or "both";
    public bool UseStoreSearch => SearchMode is "stores" or "both" || (SearchMode == "auto" && !HasShoppingKey);
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    public string? WhatsAppPhone { get; init; }
    public string? WhatsAppApiKey { get; init; }
    public string WhatsAppApiUrl { get; init; } = "https://api.callmebot.com/whatsapp.php";

    public string? TelegramBotToken { get; init; }
    public string? TelegramChatId { get; init; }

    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public bool SmtpUseSsl { get; init; } = true;
    public string? SmtpUser { get; init; }
    public string? SmtpPassword { get; init; }
    public string? EmailFrom { get; init; }
    public string? EmailTo { get; init; }

    public bool WhatsAppEnabled =>
        !string.IsNullOrWhiteSpace(WhatsAppPhone) && !string.IsNullOrWhiteSpace(WhatsAppApiKey);

    public bool TelegramEnabled =>
        !string.IsNullOrWhiteSpace(TelegramBotToken) && !string.IsNullOrWhiteSpace(TelegramChatId);

    public bool EmailEnabled =>
        !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(EmailTo);

    public static Settings FromEnvironment(string? dotEnvPath = ".env")
    {
        if (dotEnvPath is not null)
            LoadDotEnv(dotEnvPath);

        return new Settings
        {
            WatchlistPath = Env("WATCHLIST_PATH") ?? "watchlist.json",
            StoresPath = Env("STORES_PATH") ?? "stores.json",
            HistoryPath = Env("HISTORY_PATH") ?? "data/history.json",
            ReportPath = Env("REPORT_PATH") ?? "data/report.html",
            MinDropPercent = ParseDecimal(Env("MIN_DROP_PERCENT")) ?? 0m,
            RequestTimeoutSeconds = ParseInt(Env("REQUEST_TIMEOUT_SECONDS")) ?? 20,
            MaxOffersInAlert = ParseInt(Env("MAX_OFFERS_IN_ALERT")) ?? 5,
            DebugDir = Env("DEBUG_DIR"),
            SerperApiKey = Env("SERPER_API_KEY"),
            SerpApiKey = Env("SERPAPI_API_KEY"),
            ApiPauseUntil = DateOnly.TryParseExact(Env("API_PAUSE_UNTIL"), "yyyy-MM-dd", out var pause) ? pause : null,
            SerperLocation = Env("SERPER_LOCATION") ?? "Spain",
            SerperEndpoint = Env("SERPER_ENDPOINT"),
            SearchMode = (Env("SEARCH_MODE") ?? "auto").ToLowerInvariant(),
            WebSearch = !string.Equals(Env("WEB_SEARCH"), "false", StringComparison.OrdinalIgnoreCase),
            WebSearchMaxPages = ParseInt(Env("WEB_SEARCH_MAX_PAGES")) ?? 10,
            WhatsAppPhone = Env("WHATSAPP_PHONE"),
            WhatsAppApiKey = Env("WHATSAPP_APIKEY"),
            WhatsAppApiUrl = Env("WHATSAPP_API_URL") ?? "https://api.callmebot.com/whatsapp.php",
            TelegramBotToken = Env("TELEGRAM_BOT_TOKEN"),
            TelegramChatId = Env("TELEGRAM_CHAT_ID"),
            SmtpHost = Env("SMTP_HOST"),
            SmtpPort = ParseInt(Env("SMTP_PORT")) ?? 587,
            SmtpUseSsl = !string.Equals(Env("SMTP_USE_SSL"), "false", StringComparison.OrdinalIgnoreCase),
            SmtpUser = Env("SMTP_USER"),
            SmtpPassword = Env("SMTP_PASSWORD"),
            EmailFrom = Env("EMAIL_FROM") ?? Env("SMTP_USER"),
            EmailTo = Env("EMAIL_TO"),
        };
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>Carga un archivo .env (KEY=VALUE) sin pisar variables ya definidas en el entorno.</summary>
    internal static void LoadDotEnv(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
