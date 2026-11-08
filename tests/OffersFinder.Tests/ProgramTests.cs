using Xunit;

namespace OffersFinder.Tests;

public class ProgramTests
{
    [Theory]
    [InlineData("30m", 30)]
    [InlineData("2h", 120)]
    [InlineData("1d", 1440)]
    [InlineData("90s", 1.5)]
    [InlineData("45", 45)]
    public void Parses_intervals(string text, double minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), Program.ParseInterval(text));
    }

    [Fact]
    public void Invalid_interval_is_null()
    {
        Assert.Null(Program.ParseInterval("pronto"));
        Assert.Null(Program.ParseInterval(""));
    }
}
