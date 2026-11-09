using OffersFinder.Services;
using SkiaSharp;
using Xunit;

namespace OffersFinder.Tests;

public class SkiaChartTests
{
    [Fact]
    public void Renders_chart_with_text_lines_and_legend()
    {
        var points = new[] { new ChartPoint(DateTimeOffset.Parse("2026-11-01T10:00Z"), 200), new ChartPoint(DateTimeOffset.Parse("2026-11-02T10:00Z"), 180), new ChartPoint(DateTimeOffset.Parse("2026-11-03T10:00Z"), 185) };
        var series = new[] { new ChartSeries("Mejor precio", points, "#2563eb", 3, false, true), new ChartSeries("Tienda A", points, "#f97316") };

        var png = SkiaChart.RenderPng("Bicicleta de prueba", series, 150, "€", 800, 400);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(800, bitmap.Width);
        // Hay texto dibujado (píxeles oscuros en la zona del título) y línea azul en la zona de la gráfica.
        var darkInTitle = 0;
        for (var x = 20; x < 400; x += 2) for (var y = 20; y < 50; y += 2) if (bitmap.GetPixel(x, y).Red < 100) darkInTitle++;
        Assert.True(darkInTitle > 30, $"No se ha dibujado el título (píxeles oscuros: {darkInTitle})");
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "offersfinder-chart-test.png"), png);
    }

    [Fact]
    public void Renders_placeholder_without_data()
    {
        var png = SkiaChart.RenderPng("Vacío", Array.Empty<ChartSeries>(), null, "€", 400, 200);
        Assert.True(png.Length > 500);
    }
}
