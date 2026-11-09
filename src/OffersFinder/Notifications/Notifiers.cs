using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using OffersFinder.Models;

namespace OffersFinder.Notifications;

public sealed record Alert(string Title, string Body, string? Url);

public interface INotifier
{
    string Name { get; }

    Task SendAsync(Alert alert, CancellationToken ct = default);

    /// <summary>Envía un archivo (por ejemplo el informe HTML). Por defecto no hace nada; Telegram y email lo adjuntan.</summary>
    Task SendFileAsync(string path, string caption, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Envía varios avisos de una ejecución. Por defecto, uno a uno; el email los agrupa en un solo correo.</summary>
    async Task SendManyAsync(IReadOnlyList<Alert> alerts, CancellationToken ct = default)
    {
        foreach (var alert in alerts)
            await SendAsync(alert, ct);
    }
}

/// <summary>Escribe el aviso en la consola (siempre activo).</summary>
public sealed class ConsoleNotifier : INotifier
{
    public string Name => "consola";

    public Task SendFileAsync(string path, string caption, CancellationToken ct = default)
    {
        Console.WriteLine($"📎 {caption}: {Path.GetFullPath(path)}");
        return Task.CompletedTask;
    }

    public Task SendAsync(Alert alert, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine($"🔔 {alert.Title}");
        Console.WriteLine(alert.Body);
        if (alert.Url is not null) Console.WriteLine(alert.Url);
        Console.WriteLine();
        return Task.CompletedTask;
    }
}

/// <summary>Envía el aviso por Telegram usando un bot.</summary>
public sealed class TelegramNotifier : INotifier
{
    private readonly string _token;
    private readonly string _chatId;
    private readonly HttpClient _http;

    public TelegramNotifier(string token, string chatId, HttpClient? http = null)
    {
        _token = token;
        _chatId = chatId;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public string Name => "Telegram";

    public async Task SendAsync(Alert alert, CancellationToken ct = default)
    {
        var text = $"*{Escape(alert.Title)}*\n{Escape(alert.Body)}";
        if (alert.Url is not null) text += $"\n{Escape(alert.Url)}";

        var payload = JsonSerializer.Serialize(new
        {
            chat_id = _chatId,
            text,
            parse_mode = "MarkdownV2",
            disable_web_page_preview = false,
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"https://api.telegram.org/bot{_token}/sendMessage", content, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    public async Task SendFileAsync(string path, string caption, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_chatId), "chat_id");
        form.Add(new StringContent(caption), "caption");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
        form.Add(file, "document", Path.GetFileName(path));
        using var response = await _http.PostAsync($"https://api.telegram.org/bot{_token}/sendDocument", form, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ("_*[]()~`>#+-=|{}.!\\".Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public sealed record TelegramChat(long Id, string Description);

    /// <summary>Consulta los últimos mensajes recibidos por el bot y devuelve los chats que le han escrito.</summary>
    public static async Task<List<TelegramChat>> DiscoverChatsAsync(string token, HttpClient? http = null, CancellationToken ct = default)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await http.GetAsync($"https://api.telegram.org/bot{token}/getUpdates", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram respondió {(int)response.StatusCode}: {json}");
        return ParseChats(json);
    }

    public static List<TelegramChat> ParseChats(string getUpdatesJson)
    {
        var chats = new Dictionary<long, TelegramChat>();
        using var doc = JsonDocument.Parse(getUpdatesJson);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return new List<TelegramChat>();

        foreach (var update in result.EnumerateArray())
        {
            foreach (var key in new[] { "message", "edited_message", "channel_post", "my_chat_member" })
            {
                if (!update.TryGetProperty(key, out var container) || !container.TryGetProperty("chat", out var chat)) continue;
                if (!chat.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id)) continue;

                var name = string.Join(' ', new[] { Str(chat, "first_name"), Str(chat, "last_name") }.Where(x => x is not null));
                var username = Str(chat, "username");
                var title = Str(chat, "title");
                var type = Str(chat, "type") ?? "chat";
                var description = title ?? (name.Length > 0 ? name : username ?? "sin nombre");
                if (username is not null && title is null && name.Length > 0) description += $" (@{username})";
                chats[id] = new TelegramChat(id, $"{description} [{type}]");
            }
        }
        return chats.Values.ToList();
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Envía el aviso a tu WhatsApp a través de CallMeBot (servicio gratuito para uso personal).
/// Alta: añade el número de CallMeBot a tus contactos, envíale "I allow callmebot to send me messages"
/// y recibirás tu apikey. Detalles en https://www.callmebot.com/blog/free-api-whatsapp-messages/
/// </summary>
public sealed class WhatsAppNotifier : INotifier
{
    private readonly string _phone;
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly HttpClient _http;

    public WhatsAppNotifier(string phone, string apiKey, string apiUrl, HttpClient? http = null)
    {
        _phone = phone;
        _apiKey = apiKey;
        _apiUrl = apiUrl;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string Name => "WhatsApp";

    public async Task SendAsync(Alert alert, CancellationToken ct = default)
    {
        var text = $"*{alert.Title}*\n{alert.Body}";
        if (alert.Url is not null) text += $"\n{alert.Url}";

        var url = $"{_apiUrl}?phone={Uri.EscapeDataString(_phone)}&apikey={Uri.EscapeDataString(_apiKey)}&text={Uri.EscapeDataString(text)}";
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CallMeBot respondió {(int)response.StatusCode}: {Summarize(body)}");
        if (body.Contains("APIKey is invalid", StringComparison.OrdinalIgnoreCase) || body.Contains("not registered", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"CallMeBot rechazó el envío: {Summarize(body)}");
    }

    public async Task SendManyAsync(IReadOnlyList<Alert> alerts, CancellationToken ct = default)
    {
        for (var i = 0; i < alerts.Count; i++)
        {
            if (i > 0) await Task.Delay(TimeSpan.FromSeconds(2), ct); // evitar el límite de envío del servicio
            await SendAsync(alerts[i], ct);
        }
    }

    private static string Summarize(string body)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 160 ? text[..160] + "…" : text;
    }
}

/// <summary>Envía los avisos por correo electrónico mediante SMTP: un solo correo por ejecución, en texto y HTML.</summary>
public sealed class EmailNotifier : INotifier
{
    private readonly Settings _settings;
    public EmailNotifier(Settings settings) => _settings = settings;

    public string Name => "email";

    public Task SendAsync(Alert alert, CancellationToken ct = default) => SendManyAsync(new[] { alert }, ct);

    public async Task SendManyAsync(IReadOnlyList<Alert> alerts, CancellationToken ct = default)
    {
        if (alerts.Count == 0) return;

        var subject = alerts.Count == 1
            ? alerts[0].Title
            : $"🛒 OffersFinder: {alerts.Count} avisos de precio";

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.SmtpUseSsl,
            Credentials = string.IsNullOrEmpty(_settings.SmtpUser)
                ? null
                : new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword),
        };

        var from = _settings.EmailFrom ?? _settings.SmtpUser ?? "offersfinder@localhost";
        using var message = new MailMessage
        {
            From = new MailAddress(from, "OffersFinder"),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = BuildText(alerts),
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        foreach (var to in _settings.EmailTo!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(to);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildHtml(alerts), Encoding.UTF8, "text/html"));

        await client.SendMailAsync(message, ct);
    }

    public async Task SendFileAsync(string path, string caption, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.SmtpUseSsl,
            Credentials = string.IsNullOrEmpty(_settings.SmtpUser) ? null : new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword),
        };
        var from = _settings.EmailFrom ?? _settings.SmtpUser ?? "offersfinder@localhost";
        using var message = new MailMessage { From = new MailAddress(from, "OffersFinder"), Subject = caption, Body = caption, BodyEncoding = Encoding.UTF8 };
        foreach (var to in _settings.EmailTo!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(to);
        message.Attachments.Add(new Attachment(path));
        await client.SendMailAsync(message, ct);
    }

    public static string BuildText(IReadOnlyList<Alert> alerts)
    {
        var sb = new StringBuilder();
        foreach (var alert in alerts)
        {
            sb.AppendLine(alert.Title);
            sb.AppendLine(new string('-', Math.Min(alert.Title.Length, 60)));
            sb.AppendLine(alert.Body);
            if (alert.Url is not null) sb.AppendLine(alert.Url);
            sb.AppendLine();
        }
        sb.AppendLine("Enviado por OffersFinder.");
        return sb.ToString();
    }

    public static string BuildHtml(IReadOnlyList<Alert> alerts)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><body style=\"font-family:system-ui,Segoe UI,Roboto,sans-serif;color:#1f2933;max-width:640px;margin:0 auto;padding:16px\">");
        foreach (var alert in alerts)
        {
            sb.Append("<div style=\"border:1px solid #e5e7eb;border-radius:10px;padding:16px;margin-bottom:16px\">");
            sb.Append("<h2 style=\"margin:0 0 8px;font-size:18px\">").Append(Html(alert.Title)).Append("</h2>");
            sb.Append("<div style=\"white-space:pre-wrap;line-height:1.5\">").Append(Linkify(alert.Body)).Append("</div>");
            if (alert.Url is not null)
                sb.Append("<p style=\"margin:12px 0 0\"><a href=\"").Append(Html(alert.Url)).Append("\" style=\"color:#2563eb\">Ver producto</a></p>");
            sb.Append("</div>");
        }
        sb.Append("<p style=\"color:#6b7280;font-size:12px\">Enviado por OffersFinder.</p></body></html>");
        return sb.ToString();
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    /// <summary>Escapa el texto y convierte las URLs en enlaces.</summary>
    private static string Linkify(string text)
    {
        var sb = new StringBuilder();
        foreach (var token in text.Split(' '))
        {
            var trimmed = token.TrimEnd('\n', '\r');
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var tail = token[trimmed.Length..];
                sb.Append("<a href=\"").Append(Html(trimmed)).Append("\" style=\"color:#2563eb\">").Append(Html(trimmed)).Append("</a>").Append(tail);
            }
            else
            {
                sb.Append(Html(token));
            }
            sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
}

public static class NotifierFactory
{
    public static List<INotifier> FromSettings(Settings settings)
    {
        var notifiers = new List<INotifier> { new ConsoleNotifier() };
        if (settings.TelegramEnabled)
            notifiers.Add(new TelegramNotifier(settings.TelegramBotToken!, settings.TelegramChatId!));
        if (settings.EmailEnabled)
            notifiers.Add(new EmailNotifier(settings));
        if (settings.WhatsAppEnabled)
            notifiers.Add(new WhatsAppNotifier(settings.WhatsAppPhone!, settings.WhatsAppApiKey!, settings.WhatsAppApiUrl));
        return notifiers;
    }
}
