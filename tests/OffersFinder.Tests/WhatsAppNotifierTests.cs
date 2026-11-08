using System.Net;
using OffersFinder.Notifications;
using Xunit;

namespace OffersFinder.Tests;

public class WhatsAppNotifierTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "<p>Message queued.</p>";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    [Fact]
    public async Task Sends_phone_apikey_and_text_as_query()
    {
        var handler = new FakeHandler();
        var notifier = new WhatsAppNotifier("+34600000000", "123456", "https://bot.example/whatsapp.php", new HttpClient(handler));

        await notifier.SendAsync(new Alert("⬇️ Bajada", "Antes 10 € → Ahora 8 €", "https://tienda.example/p"));

        var uri = Assert.Single(handler.Requests);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("+34600000000", query["phone"]);
        Assert.Equal("123456", query["apikey"]);
        Assert.Contains("*⬇️ Bajada*", query["text"]);
        Assert.Contains("https://tienda.example/p", query["text"]);
    }

    [Fact]
    public async Task Fails_on_http_error_or_invalid_key()
    {
        var handler = new FakeHandler { Status = HttpStatusCode.InternalServerError };
        var notifier = new WhatsAppNotifier("+34", "k", "https://bot.example/w", new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendAsync(new Alert("t", "b", null)));

        handler = new FakeHandler { Body = "<html>APIKey is invalid</html>" };
        notifier = new WhatsAppNotifier("+34", "k", "https://bot.example/w", new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendAsync(new Alert("t", "b", null)));
        Assert.Contains("APIKey is invalid", ex.Message);
    }
}
