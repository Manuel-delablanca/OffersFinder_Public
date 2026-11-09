using OffersFinder.Models;
using OffersFinder.Services;
using Xunit;

namespace OffersFinder.Tests;

public class FilterItemsTests
{
    private static readonly List<WatchItem> Items = new()
    {
        new() { Name = "Bicicleta Orbea MX 12", Query = "Orbea MX 12" },
        new() { Name = "Casco Micro PC Helmet Flamingo (talla S)", Query = "Micro PC Helmet Flamingo" },
        new() { Name = "Molinillo HiBREW G3", Query = "HiBREW G3" },
    };

    [Fact]
    public void Without_filters_returns_all()
    {
        Assert.Equal(3, TextMatch.FilterItems(Items, Array.Empty<string>()).Count);
    }

    [Fact]
    public void Filters_by_name_substring_query_or_index()
    {
        Assert.Equal(new[] { "Casco Micro PC Helmet Flamingo (talla S)" }, TextMatch.FilterItems(Items, new[] { "flamingo" }).Select(i => i.Name));
        Assert.Equal(new[] { "Molinillo HiBREW G3" }, TextMatch.FilterItems(Items, new[] { "hibrew" }).Select(i => i.Name));
        Assert.Equal(2, TextMatch.FilterItems(Items, new[] { "casco", "1" }).Count);
        Assert.Empty(TextMatch.FilterItems(Items, new[] { "strider" }));
    }
}
