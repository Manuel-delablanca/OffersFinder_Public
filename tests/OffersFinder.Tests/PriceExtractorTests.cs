using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class PriceExtractorTests
{
    [Fact]
    public void Uses_user_selector_first()
    {
        var html = """
            <html><body>
            <meta property="product:price:amount" content="999">
            <span class="my-price">149,99 €</span>
            </body></html>
            """;
        Assert.Equal(149.99m, PriceExtractor.Extract(html, ".my-price"));
    }

    [Fact]
    public void Reads_json_ld_offer_price()
    {
        var html = """
            <html><head>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Product","name":"SSD","offers":{"@type":"Offer","price":"159.90","priceCurrency":"EUR"}}
            </script>
            </head><body><span class="price">999 €</span></body></html>
            """;
        Assert.Equal(159.90m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Reads_json_ld_aggregate_low_price()
    {
        var html = """
            <script type="application/ld+json">
            [{"@type":"Product","offers":{"@type":"AggregateOffer","lowPrice":120.5,"highPrice":180}}]
            </script>
            """;
        Assert.Equal(120.5m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Falls_back_to_meta_tags()
    {
        var html = """<html><head><meta property="og:price:amount" content="79.99"></head><body></body></html>""";
        Assert.Equal(79.99m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Falls_back_to_itemprop()
    {
        var html = """<div itemprop="offers"><span itemprop="price" content="45.00">45,00 €</span></div>""";
        Assert.Equal(45m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Falls_back_to_price_class()
    {
        var html = """<div class="product"><div class="price-current">1.099,00 €</div></div>""";
        Assert.Equal(1099m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Ignores_malformed_json_ld()
    {
        var html = """
            <script type="application/ld+json">{ not json</script>
            <meta itemprop="price" content="10">
            """;
        Assert.Equal(10m, PriceExtractor.Extract(html));
    }

    [Fact]
    public void Returns_null_when_nothing_found()
    {
        Assert.Null(PriceExtractor.Extract("<html><body><p>Hola</p></body></html>"));
    }
}
