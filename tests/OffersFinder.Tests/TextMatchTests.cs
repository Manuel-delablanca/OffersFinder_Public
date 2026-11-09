using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("Samsung 990 PRO 2 TB NVMe", "samsung990pro2tb nvme")]
    [InlineData("Orbea MX 12 Azul", "orbea mx12azul")]
    [InlineData("Bicicleta MX-12 niño", "bicicleta mx12nino")]
    [InlineData("  Strider   12  Sport ", "strider12sport")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizes_case_accents_and_number_unit_gaps(string? input, string expected)
    {
        Assert.Equal(expected, TextMatch.Normalize(input));
    }

    [Fact]
    public void Matches_terms_regardless_of_spacing_and_case()
    {
        var item = new WatchItem { Query = "Samsung 990 Pro 2TB" };
        Assert.True(OfferSearcher.Matches(item, new("s", "S", "SSD SAMSUNG 990 PRO 2 TB", 150, "u", false)));
        Assert.True(OfferSearcher.Matches(item, new("s", "S", "Samsung 990PRO 2TB", 150, "u", false)));
        Assert.False(OfferSearcher.Matches(item, new("s", "S", "Samsung 980 Pro 2 TB", 150, "u", false)));
    }

    [Fact]
    public void Supports_synonyms_with_pipe()
    {
        var item = new WatchItem { Query = "x", MustContain = new() { "strider", "12|12 pulgadas|12\"" } };
        Assert.True(OfferSearcher.Matches(item, new("s", "S", "Bici Strider de 12 pulgadas", 100, "u", false)));
        Assert.True(OfferSearcher.Matches(item, new("s", "S", "Strider 12\" Sport", 100, "u", false)));
        Assert.False(OfferSearcher.Matches(item, new("s", "S", "Strider 14x Sport", 100, "u", false)));
    }

    [Fact]
    public void Exclusions_are_tolerant_too()
    {
        var item = new WatchItem { Query = "Orbea MX 12", Exclude = new() { "mx 16", "rosa" } };
        Assert.Null(TextMatch.FirstExcluded("Orbea MX 12", item.Exclude));
        Assert.Equal("mx 16", TextMatch.FirstExcluded("Orbea MX16 2024", item.Exclude));
        Assert.Equal("rosa", TextMatch.FirstExcluded("Orbea MX 12 Rosa", item.Exclude));
        Assert.Equal(new[] { "12" }, TextMatch.MissingTerms("Orbea MX 16", TextMatch.RequiredTerms(item)));
    }
}
