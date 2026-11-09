using System.Globalization;
using System.Text;

namespace OffersFinder.Services;

public sealed record ChartPoint(DateTimeOffset Time, decimal Price);
public sealed record ChartSeries(string Label, IReadOnlyList<ChartPoint> Points, string Color, double Width = 1.5, bool Dashed = false, bool Area = false);

/// <summary>Gráfica de líneas en SVG puro (sin JavaScript), legible en cualquier visor de HTML.</summary>
public static class SvgChart
{
    public static readonly string[] Palette = { "#0ea5e9", "#f97316", "#84cc16", "#ec4899", "#a855f7", "#14b8a6" };

    public static string Render(IReadOnlyList<ChartSeries> series, decimal? target, string currency, int width = 640, int height = 320)
    {
        var all = series.SelectMany(s => s.Points).ToList();
        if (all.Count == 0)
            return $"<svg class=\"chart\" viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\"><text x=\"{width / 2}\" y=\"{height / 2}\" text-anchor=\"middle\" class=\"muted\">Sin datos</text></svg>";

        const int left = 78, right = 14, top = 18, bottom = 44;
        var plotW = width - left - right;
        var plotH = height - top - bottom;

        var times = all.Select(p => p.Time).Distinct().OrderBy(t => t).ToList();
        var minT = times[0].ToUnixTimeSeconds();
        var maxT = times[^1].ToUnixTimeSeconds();
        var spanT = Math.Max(1, maxT - minT);

        var prices = all.Select(p => p.Price).ToList();
        if (target is { } t0) prices.Add(t0);
        var minP = prices.Min();
        var maxP = prices.Max();
        if (minP == maxP) { minP -= 1; maxP += 1; }
        var pad = (maxP - minP) * 0.08m;
        minP -= pad; maxP += pad;

        double X(DateTimeOffset t) => times.Count == 1 ? left + plotW / 2.0 : left + (double)(t.ToUnixTimeSeconds() - minT) / spanT * plotW;
        double Y(decimal p) => top + (double)((maxP - p) / (maxP - minP)) * plotH;
        string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"<svg class=\"chart\" viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"Evolución del precio\">");

        // Rejilla y eje Y (5 marcas)
        for (var i = 0; i <= 4; i++)
        {
            var p = minP + (maxP - minP) * i / 4;
            var y = Y(p);
            sb.Append($"<line x1=\"{left}\" y1=\"{F(y)}\" x2=\"{width - right}\" y2=\"{F(y)}\" class=\"grid\"/>");
            sb.Append($"<text x=\"{left - 8}\" y=\"{F(y + 4)}\" text-anchor=\"end\" class=\"tick\">{Esc(PriceParser.Format(Math.Round(p, 2), currency))}</text>");
        }

        // Eje X: hasta 6 fechas repartidas
        var labelCount = Math.Min(6, times.Count);
        for (var i = 0; i < labelCount; i++)
        {
            var idx = labelCount == 1 ? 0 : (int)Math.Round(i * (times.Count - 1) / (double)(labelCount - 1));
            var time = times[idx];
            var anchor = labelCount == 1 ? "middle" : i == 0 ? "start" : i == labelCount - 1 ? "end" : "middle";
            sb.Append($"<text x=\"{F(X(time))}\" y=\"{height - 12}\" text-anchor=\"{anchor}\" class=\"tick\">{time.ToLocalTime():dd/MM HH:mm}</text>");
        }

        if (target is { } tgt)
        {
            var y = F(Y(tgt));
            sb.Append($"<line x1=\"{left}\" y1=\"{y}\" x2=\"{width - right}\" y2=\"{y}\" stroke=\"#0891b2\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\"/>");
            sb.Append($"<text x=\"{width - right}\" y=\"{F(Y(tgt) - 5)}\" text-anchor=\"end\" font-size=\"13\" fill=\"#0891b2\">objetivo {Esc(PriceParser.Format(tgt, currency))}</text>");
        }

        foreach (var s in series)
        {
            var pts = s.Points.OrderBy(p => p.Time).ToList();
            if (pts.Count == 0) continue;
            var path = string.Join(" ", pts.Select(p => $"{F(X(p.Time))},{F(Y(p.Price))}"));
            if (s.Area && pts.Count > 1)
            {
                var baseline = F(top + plotH);
                sb.Append($"<polygon points=\"{F(X(pts[0].Time))},{baseline} {path} {F(X(pts[^1].Time))},{baseline}\" fill=\"{s.Color}\" fill-opacity=\"0.10\"/>");
            }
            if (pts.Count > 1)
                sb.Append($"<polyline points=\"{path}\" fill=\"none\" stroke=\"{s.Color}\" stroke-width=\"{F(s.Width)}\"{(s.Dashed ? " stroke-dasharray=\"6 4\"" : "")} stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
            foreach (var p in pts)
                sb.Append($"<circle cx=\"{F(X(p.Time))}\" cy=\"{F(Y(p.Price))}\" r=\"{(s.Width >= 2.5 ? 3.5 : 2.5)}\" fill=\"{s.Color}\"><title>{Esc(s.Label)}: {Esc(PriceParser.Format(p.Price, currency))} ({p.Time.ToLocalTime():dd/MM/yyyy HH:mm})</title></circle>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string Esc(string text) => System.Net.WebUtility.HtmlEncode(text);
}
