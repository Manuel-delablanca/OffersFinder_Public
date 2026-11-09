using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class AlsoAcceptTests
{
    private static readonly WatchItem Casco = new()
    {
        Query = "Casco Micro Flamenco",
        MustContain = new() { "micro", "flamingo|flamenco", "helmet|casco" },
        AlsoAccept = new() { new() { "casco", "micro" } },
        Exclude = new() { "neochrome", "negro|black", "talla m" },
    };

    [Fact]
    public void Exact_match_is_unmarked_and_loose_match_is_marked()
    {
        var exact = OfferSearcher.Accept(Casco, new("eci", "ECI", "Casco Micro Flamenco S", 54.99m, "u", true));
        Assert.NotNull(exact);
        Assert.DoesNotContain("sin confirmar", exact!.Title);

        var loose = OfferSearcher.Accept(Casco, new("eci", "ECI", "Casco Micro", 54.99m, "u", true));
        Assert.NotNull(loose);
        Assert.EndsWith("(modelo sin confirmar)", loose!.Title);
    }

    [Fact]
    public void Exclusions_still_apply_to_loose_matches()
    {
        Assert.Null(OfferSearcher.Accept(Casco, new("m", "Mamut", "Casco Micro Neochrome", 52.90m, "u", false)));
        Assert.Null(OfferSearcher.Accept(Casco, new("m", "Mamut", "Casco Micro negro talla M", 39.90m, "u", false)));
        Assert.Null(OfferSearcher.Accept(Casco, new("r", "Rollers", "Casco Integral Azul", 59.90m, "u", false)));
    }
}
