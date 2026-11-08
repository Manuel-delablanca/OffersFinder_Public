using OffersFinder.Notifications;
using Xunit;

namespace OffersFinder.Tests;

public class EmailNotifierTests
{
    private static readonly Alert Drop = new("⬇️ Bajada: SSD <2TB>", "Antes: 179,99 € → Ahora: 165,00 €\nMejores ofertas:\n• Tienda: 165,00 €\n  https://tienda.example/p?a=1&b=2", "https://tienda.example/p");

    [Fact]
    public void Html_escapes_text_and_links_urls()
    {
        var html = EmailNotifier.BuildHtml(new[] { Drop });

        Assert.Contains("SSD &lt;2TB&gt;", html);
        Assert.Contains("<a href=\"https://tienda.example/p?a=1&amp;b=2\"", html);
        Assert.Contains("Ver producto", html);
        Assert.DoesNotContain("<2TB>", html);
    }

    [Fact]
    public void Text_contains_every_alert()
    {
        var text = EmailNotifier.BuildText(new[] { Drop, new Alert("🎯 Objetivo", "Cuerpo", null) });

        Assert.Contains("Bajada: SSD <2TB>", text);
        Assert.Contains("🎯 Objetivo", text);
        Assert.Contains("https://tienda.example/p", text);
    }
}
