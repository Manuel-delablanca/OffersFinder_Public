using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class PriceParserTests
{
    [Theory]
    [InlineData("1.234,56 €", 1234.56)]
    [InlineData("1 234,56 €", 1234.56)]
    [InlineData("1.299 €", 1299)]
    [InlineData("1,299.99", 1299.99)]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("49,99€", 49.99)]
    [InlineData("Precio: 199 €", 199)]
    [InlineData("1.234.567,89 €", 1234567.89)]
    [InlineData("12,5", 12.5)]
    [InlineData("  299  ", 299)]
    [InlineData("1,299", 1299)]
    public void Parses_common_price_formats(string text, decimal expected)
    {
        Assert.Equal(expected, PriceParser.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sin stock")]
    [InlineData("0")]
    [InlineData(null)]
    public void Returns_null_when_no_price(string? text)
    {
        Assert.Null(PriceParser.Parse(text));
    }

    [Fact]
    public void Formats_euros_spanish_style()
    {
        Assert.Equal("1.234,50 €", PriceParser.Format(1234.5m));
    }
}

public class PriceFormatTests
{
    [Theory]
    [InlineData(0.5, "0,50 €")]
    [InlineData(999, "999,00 €")]
    [InlineData(1234.5, "1.234,50 €")]
    [InlineData(1234567.891, "1.234.567,89 €")]
    [InlineData(-10.25, "-10,25 €")]
    public void Formats_spanish_style(double price, string expected)
    {
        Assert.Equal(expected, PriceParser.Format((decimal)price));
    }

    [Fact]
    public void Formats_other_currencies_with_prefix()
    {
        Assert.Equal("$1.200,00", PriceParser.Format(1200m, "$"));
    }
}
