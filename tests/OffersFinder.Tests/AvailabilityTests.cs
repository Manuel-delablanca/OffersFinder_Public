using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class AvailabilityTests
{
    [Theory]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","offers":{"availability":"https://schema.org/OutOfStock","price":"10"}}</script>""", Availability.OutOfStock)]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","offers":{"availability":"http://schema.org/InStock","price":"10"}}</script>""", Availability.InStock)]
    [InlineData("""<meta property="product:availability" content="out of stock">""", Availability.OutOfStock)]
    [InlineData("""<meta property="product:availability" content="instock">""", Availability.InStock)]
    [InlineData("""<link itemprop="availability" href="https://schema.org/SoldOut">""", Availability.OutOfStock)]
    [InlineData("""<div class="product-stock">Producto agotado</div>""", Availability.OutOfStock)]
    [InlineData("""<span class="availability">En stock, envío 24h</span>""", Availability.InStock)]
    [InlineData("""<div class="description">Este producto a veces está agotado en otras tiendas</div>""", Availability.Unknown)]
    [InlineData("""<p>Hola</p>""", Availability.Unknown)]
    public void Detects_availability(string html, Availability expected)
    {
        Assert.Equal(expected, PriceExtractor.ExtractAvailability(PriceExtractor.ParseDocument(html)));
    }
}
