using System.Globalization;
using SkiaSharp;

namespace OffersFinder.Services;

/// <summary>Dibuja la gráfica de precios como imagen PNG con SkiaSharp (para enviarla como foto por Telegram).</summary>
public static class SkiaChart
{
    private static readonly string[] FontCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
    };

    private static readonly Lazy<SKTypeface> Typeface = new(LoadTypeface);

    /// <summary>Busca una fuente del sistema: la versión "sin dependencias" de SkiaSharp no usa fontconfig y, sin un archivo de fuente, no dibuja texto.</summary>
    private static SKTypeface LoadTypeface()
    {
        var env = Environment.GetEnvironmentVariable("CHART_FONT");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env) && SKTypeface.FromFile(env) is { } fromEnv) return fromEnv;
        foreach (var path in FontCandidates)
            if (File.Exists(path) && SKTypeface.FromFile(path) is { } tf) return tf;
        foreach (var dir in new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts") })
        {
            if (!Directory.Exists(dir)) continue;
            var file = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(f => (f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)) &&
                                     !f.Contains("Bold", StringComparison.OrdinalIgnoreCase) && !f.Contains("Italic", StringComparison.OrdinalIgnoreCase) && !f.Contains("Mono", StringComparison.OrdinalIgnoreCase));
            if (file is not null && SKTypeface.FromFile(file) is { } tf) return tf;
        }
        return SKTypeface.FromFamilyName("sans-serif") ?? SKTypeface.Default;
    }

    public static byte[] RenderPng(string title, IReadOnlyList<ChartSeries> series, decimal? target, string currency, int width = 1200, int height = 600)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var typeface = Typeface.Value;
        using var titleFont = new SKFont(typeface, 30);
        using var tickFont = new SKFont(typeface, 20);
        using var legendFont = new SKFont(typeface, 20);
        using var text = new SKPaint { Color = SKColor.Parse("#1f2933"), IsAntialias = true };
        using var muted = new SKPaint { Color = SKColor.Parse("#6b7280"), IsAntialias = true };
        using var grid = new SKPaint { Color = SKColor.Parse("#e5e7eb"), StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };

        canvas.DrawText(title, 24, 44, SKTextAlign.Left, titleFont, text);

        var all = series.SelectMany(s => s.Points).ToList();
        const float left = 130, right = 30, top = 70, bottom = 110;
        var plotW = width - left - right;
        var plotH = height - top - bottom;

        if (all.Count == 0)
        {
            canvas.DrawText("Sin datos", width / 2f, height / 2f, SKTextAlign.Center, titleFont, muted);
            return Encode(bitmap);
        }

        var times = all.Select(p => p.Time).Distinct().OrderBy(t => t).ToList();
        var minT = times[0].ToUnixTimeSeconds();
        var spanT = Math.Max(1, times[^1].ToUnixTimeSeconds() - minT);
        var prices = all.Select(p => p.Price).ToList();
        if (target is { } t0) prices.Add(t0);
        var minP = prices.Min(); var maxP = prices.Max();
        if (minP == maxP) { minP -= 1; maxP += 1; }
        var pad = (maxP - minP) * 0.08m; minP -= pad; maxP += pad;

        float X(DateTimeOffset t) => times.Count == 1 ? left + plotW / 2 : left + (float)(t.ToUnixTimeSeconds() - minT) / spanT * plotW;
        float Y(decimal p) => top + (float)((maxP - p) / (maxP - minP)) * plotH;

        for (var i = 0; i <= 4; i++)
        {
            var p = minP + (maxP - minP) * i / 4;
            canvas.DrawLine(left, Y(p), width - right, Y(p), grid);
            canvas.DrawText(PriceParser.Format(Math.Round(p, 2), currency), left - 12, Y(p) + 7, SKTextAlign.Right, tickFont, muted);
        }
        var labelCount = Math.Min(6, times.Count);
        for (var i = 0; i < labelCount; i++)
        {
            var idx = labelCount == 1 ? 0 : (int)Math.Round(i * (times.Count - 1) / (double)(labelCount - 1));
            var align = labelCount == 1 ? SKTextAlign.Center : i == 0 ? SKTextAlign.Left : i == labelCount - 1 ? SKTextAlign.Right : SKTextAlign.Center;
            canvas.DrawText(times[idx].ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture), X(times[idx]), top + plotH + 32, align, tickFont, muted);
        }

        if (target is { } tgt)
        {
            using var targetPaint = new SKPaint { Color = SKColor.Parse("#0891b2"), StrokeWidth = 2.5f, IsAntialias = true, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash(new[] { 12f, 8f }, 0) };
            canvas.DrawLine(left, Y(tgt), width - right, Y(tgt), targetPaint);
            using var targetText = new SKPaint { Color = SKColor.Parse("#0891b2"), IsAntialias = true };
            canvas.DrawText($"objetivo {PriceParser.Format(tgt, currency)}", width - right, Y(tgt) - 8, SKTextAlign.Right, tickFont, targetText);
        }

        foreach (var s in series)
        {
            var pts = s.Points.OrderBy(p => p.Time).ToList();
            if (pts.Count == 0) continue;
            var color = SKColor.Parse(s.Color);
            using var line = new SKPaint { Color = color, StrokeWidth = (float)s.Width * 1.6f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            using var dot = new SKPaint { Color = color, IsAntialias = true };
            using var path = new SKPath();
            path.MoveTo(X(pts[0].Time), Y(pts[0].Price));
            foreach (var p in pts.Skip(1)) path.LineTo(X(p.Time), Y(p.Price));
            if (s.Area && pts.Count > 1)
            {
                using var area = new SKPath(path);
                area.LineTo(X(pts[^1].Time), top + plotH); area.LineTo(X(pts[0].Time), top + plotH); area.Close();
                using var fill = new SKPaint { Color = color.WithAlpha(28), IsAntialias = true };
                canvas.DrawPath(area, fill);
            }
            if (pts.Count > 1) canvas.DrawPath(path, line);
            foreach (var p in pts) canvas.DrawCircle(X(p.Time), Y(p.Price), s.Width >= 2.5 ? 6 : 4, dot);
        }

        // Leyenda
        var lx = left; var ly = height - 40f;
        var legend = series.Select(s => (s.Label, s.Color)).ToList();
        if (target is not null) legend.Add(("Objetivo", "#0891b2"));
        foreach (var (label, color) in legend)
        {
            using var swatch = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
            canvas.DrawRoundRect(lx, ly - 14, 18, 18, 3, 3, swatch);
            canvas.DrawText(label, lx + 26, ly, SKTextAlign.Left, legendFont, text);
            lx += 26 + legendFont.MeasureText(label) + 28;
            if (lx > width - 200) { lx = left; ly += 30; }
        }

        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
