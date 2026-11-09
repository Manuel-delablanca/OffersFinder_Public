using System.Net;
using System.Text.Json;
using OffersFinder.Notifications;
using Xunit;

namespace OffersFinder.Tests;

public class TelegramNotifierTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string Body)> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Response { get; set; } = "{\"ok\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }

    [Fact]
    public async Task Sends_markdown_message_with_escaped_text()
    {
        var handler = new FakeHandler();
        var notifier = new TelegramNotifier("TOKEN", "42", new HttpClient(handler));

        await notifier.SendAsync(new Alert("⬇️ Bajada: SSD 2TB", "Antes: 179,99 € → Ahora: 165,00 € (-8.3 %)", "https://tienda.example/p"));

        var (uri, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.telegram.org/botTOKEN/sendMessage", uri.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("42", json.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("MarkdownV2", json.RootElement.GetProperty("parse_mode").GetString());
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.StartsWith("*⬇️ Bajada: SSD 2TB*", text);
        Assert.Contains("\\(\\-8\\.3 %\\)", text);               // caracteres especiales escapados
        Assert.Contains("https://tienda\\.example/p", text);
    }

    [Fact]
    public async Task Throws_on_api_error()
    {
        var handler = new FakeHandler { Status = HttpStatusCode.BadRequest, Response = "{\"ok\":false,\"description\":\"chat not found\"}" };
        var notifier = new TelegramNotifier("TOKEN", "42", new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendAsync(new Alert("t", "b", null)));
        Assert.Contains("chat not found", ex.Message);
    }

    [Fact]
    public async Task Pins_important_alerts_after_sending()
    {
        var handler = new FakeHandler { Response = "{\"ok\":true,\"result\":{\"message_id\":77}}" };
        var notifier = new TelegramNotifier("TOKEN", "42", new HttpClient(handler));

        await notifier.SendAsync(new Alert("🎯 Objetivo", "cuerpo", null, Pin: true));

        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/pinChatMessage", handler.Requests[1].Uri.AbsolutePath);
        Assert.Contains("\"message_id\":77", handler.Requests[1].Body);

        handler.Requests.Clear();
        await notifier.SendAsync(new Alert("⬇️ Bajada", "cuerpo", null));
        Assert.Single(handler.Requests); // los avisos normales no se fijan
    }

    [Fact]
    public async Task Sends_photo_as_multipart()
    {
        var handler = new FakeHandler();
        var notifier = new TelegramNotifier("TOKEN", "42", new HttpClient(handler));
        await notifier.SendImageAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "grafica.png", "Gráfica");
        var (uri, body) = Assert.Single(handler.Requests);
        Assert.EndsWith("/sendPhoto", uri.AbsolutePath);
        Assert.Contains("name=photo", body);
        Assert.Contains("filename=grafica.png", body);
    }

    [Fact]
    public async Task Sends_document_as_multipart()
    {
        var handler = new FakeHandler();
        var notifier = new TelegramNotifier("TOKEN", "42", new HttpClient(handler));
        var path = Path.Combine(Path.GetTempPath(), $"informe-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, "<html>informe</html>");
        try
        {
            await notifier.SendFileAsync(path, "Informe");
            var (uri, body) = Assert.Single(handler.Requests);
            Assert.EndsWith("/sendDocument", uri.AbsolutePath);
            Assert.Contains("name=chat_id", body);
            Assert.Contains("42", body);
            Assert.Contains("name=document", body);
            Assert.Contains("<html>informe</html>", body);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parses_chats_from_getUpdates()
    {
        const string json = """
            {"ok":true,"result":[
              {"update_id":1,"message":{"message_id":1,"chat":{"id":123456,"first_name":"Ana","last_name":"G.","username":"anag","type":"private"},"text":"/start"}},
              {"update_id":2,"message":{"message_id":2,"chat":{"id":123456,"first_name":"Ana","last_name":"G.","username":"anag","type":"private"},"text":"hola"}},
              {"update_id":3,"my_chat_member":{"chat":{"id":-100987,"title":"Ofertas","type":"supergroup"}}}
            ]}
            """;
        var chats = TelegramNotifier.ParseChats(json);

        Assert.Equal(2, chats.Count);
        Assert.Contains(chats, c => c.Id == 123456 && c.Description == "Ana G. (@anag) [private]");
        Assert.Contains(chats, c => c.Id == -100987 && c.Description == "Ofertas [supergroup]");
    }

    [Fact]
    public void Parses_empty_updates()
    {
        Assert.Empty(TelegramNotifier.ParseChats("{\"ok\":true,\"result\":[]}"));
    }
}
