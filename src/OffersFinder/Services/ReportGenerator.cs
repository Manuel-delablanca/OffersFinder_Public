using System.Globalization;
using System.Text;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>
/// Informe HTML estático: gráficas SVG, estadísticas y tablas generadas por el programa, y filtro por mes
/// resuelto con CSS. No usa JavaScript ni recursos externos, así que se ve en cualquier visor (móvil incluido).
/// </summary>
public static class ReportGenerator
{
    private static readonly string[] Months = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    public static string Build(Watchlist watchlist, Dictionary<string, ItemHistory> history, DateTimeOffset generatedAt)
    {
        var css = new StringBuilder();
        var body = new StringBuilder();

        var blacklist = watchlist.BlacklistedStores.Count == 0 ? "" : $" · Lista negra global: {E(string.Join(", ", watchlist.BlacklistedStores))}";
        body.Append($"<header><h1>OffersFinder – evolución de precios</h1><div class=\"meta\">Generado el {generatedAt.ToLocalTime():dd/MM/yyyy HH:mm}{blacklist}</div></header><main>");

        if (watchlist.Items.Count == 0)
            body.Append("<section class=\"card empty\">No hay productos en la lista.</section>");

        for (var index = 0; index < watchlist.Items.Count; index++)
        {
            var item = watchlist.Items[index];
            history.TryGetValue(item.Key, out var h);
            var records = (h?.Records ?? new List<PriceRecord>()).OrderBy(r => r.Timestamp).ToList();
            var storeRecords = (h?.StoreRecords ?? new List<StoreRecord>()).OrderBy(r => r.Timestamp).ToList();
            var months = Statistics.AvailableMonths(records).ToList();

            body.Append("<section class=\"card\">");
            body.Append($"<h2>{E(item.DisplayName)}</h2>");
            var sub = new List<string>();
            if (item.HasQuery) sub.Add($"Búsqueda: “{E(item.Query!)}”");
            foreach (var url in item.AllUrls) sub.Add($"<a href=\"{E(url)}\">{E(Trim(url, 70))}</a>");
            if (sub.Count > 0) body.Append($"<div class=\"sub\">{string.Join(" · ", sub)}</div>");

            var tags = new List<string>();
            if (item.PickupMadridOnly) tags.Add("solo recogida en Madrid");
            if (item.TargetPrice is { } tp) tags.Add($"objetivo {PriceParser.Format(tp, item.Currency)}");
            if (item.ExcludeStores is { Count: > 0 }) tags.Add($"excluye: {string.Join(", ", item.ExcludeStores)}");
            if (tags.Count > 0) body.Append("<div>" + string.Join("", tags.Select(t => $"<span class=\"tag\">{E(t)}</span>")) + "</div>");

            // Pestañas por mes (radio + CSS)
            var tabs = new List<(string Id, string Label, string? Month)> { ($"i{index}-all", "Todos", null) };
            tabs.AddRange(months.Select(m => ($"i{index}-{m}", MonthLabel(m), (string?)m)));
            // Los radios van directamente en la sección, como hermanos de .tabs y .panels, para que
            // funcionen los selectores "#radio:checked ~ ..." sin JavaScript.
            foreach (var (id, _, _) in tabs)
                body.Append($"<input type=\"radio\" class=\"tab-radio\" name=\"i{index}\" id=\"{id}\"{(id.EndsWith("-all") ? " checked" : "")}>");
            body.Append("<div class=\"tabs\">");
            foreach (var (id, label, _) in tabs)
                body.Append($"<label for=\"{id}\">{E(label)}</label>");
            body.Append("</div>");

            body.Append("<div class=\"panels\">");
            foreach (var (id, _, month) in tabs)
            {
                css.Append($"#{id}:checked ~ .panels .{id}{{display:block}}#{id}:checked ~ .tabs label[for=\"{id}\"]{{background:#2563eb;color:#fff;border-color:#2563eb}}");
                body.Append($"<div class=\"panel {id}\">");
                body.Append(RenderPanel(item, records, storeRecords, month));
                body.Append("</div>");
            }
            body.Append("</div>");

            body.Append(RenderOffers(item, h));
            body.Append("</section>");
        }

        body.Append("</main>");
        return Template.Replace("__CSS__", css.ToString()).Replace("__BODY__", body.ToString());
    }

    /// <summary>Series de la gráfica de un producto (todo el histórico o un mes), para el informe SVG y para la imagen PNG.</summary>
    public static List<ChartSeries> ChartSeriesFor(WatchItem item, ItemHistory? h, string? month = null)
    {
        var records = (h?.Records ?? new List<PriceRecord>()).OrderBy(r => r.Timestamp).ToList();
        var storeRecords = (h?.StoreRecords ?? new List<StoreRecord>()).OrderBy(r => r.Timestamp).ToList();
        return BuildSeries(item, Statistics.FilterByMonth(records, month).ToList(), storeRecords, month).Series;
    }

    private static (List<ChartSeries> Series, List<IGrouping<string, StoreRecord>> TopStores) BuildSeries(WatchItem item, List<PriceRecord> filtered, List<StoreRecord> storeRecords, string? month)
    {
        var monthStores = storeRecords.Where(r => month is null || Statistics.FilterByMonth(new[] { new PriceRecord { Timestamp = r.Timestamp, Price = r.Price } }, month).Any()).ToList();
        var series = new List<ChartSeries>
        {
            new("Mejor precio", filtered.Select(r => new ChartPoint(r.Timestamp, r.Price)).ToList(), "#2563eb", 3, false, true),
        };
        var topStores = monthStores.GroupBy(r => r.Store).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(r => r.Price)).Take(6).ToList();
        for (var i = 0; i < topStores.Count; i++)
            series.Add(new ChartSeries(topStores[i].Key, topStores[i].Select(r => new ChartPoint(r.Timestamp, r.Price)).ToList(), SvgChart.Palette[i % SvgChart.Palette.Length]));
        return (series, topStores);
    }

    private static string RenderPanel(WatchItem item, List<PriceRecord> records, List<StoreRecord> storeRecords, string? month)
    {
        var sb = new StringBuilder();
        var filtered = Statistics.FilterByMonth(records, month).ToList();
        var stats = Statistics.Compute(filtered);
        var c = item.Currency;

        if (stats is null)
        {
            sb.Append($"<div class=\"empty\">Sin registros{(month is null ? " todavía" : " en este mes")}.</div>");
            return sb.ToString();
        }

        var change = stats.Change;
        sb.Append("<div class=\"stats\">");
        sb.Append(Stat("min", "Mínimo", PriceParser.Format(stats.Min, c)));
        sb.Append(Stat("max", "Máximo", PriceParser.Format(stats.Max, c)));
        sb.Append(Stat("mean", "Media", PriceParser.Format(stats.Mean, c)));
        sb.Append(Stat("median", "Mediana", PriceParser.Format(stats.Median, c)));
        sb.Append(Stat("", "Último", PriceParser.Format(stats.Last.Price, c) + (stats.Last.Store is null ? "" : $" · {E(stats.Last.Store)}")));
        sb.Append(Stat("", "Variación", $"{(change > 0 ? "+" : "")}{PriceParser.Format(change, c)} en {stats.Count} consultas"));
        sb.Append("</div>");

        var (series, topStores) = BuildSeries(item, filtered, storeRecords, month);

        sb.Append(SvgChart.Render(series, item.TargetPrice, c));
        sb.Append("<div class=\"legend\">");
        foreach (var s in series)
            sb.Append($"<span><i style=\"background:{s.Color}\"></i>{E(s.Label)}</span>");
        if (item.TargetPrice is not null) sb.Append("<span><i style=\"background:#0891b2\"></i>Objetivo</span>");
        sb.Append("</div>");

        if (topStores.Count > 0)
        {
            sb.Append("<table class=\"stores\"><thead><tr><th>Tienda</th><th>Último</th><th>Mínimo</th><th>Consultas</th></tr></thead><tbody>");
            foreach (var g in topStores.OrderBy(g => g.OrderByDescending(r => r.Timestamp).First().Price))
                sb.Append($"<tr><td>{E(g.Key)}</td><td class=\"price\">{PriceParser.Format(g.OrderByDescending(r => r.Timestamp).First().Price, c)}</td><td class=\"price\">{PriceParser.Format(g.Min(r => r.Price), c)}</td><td>{g.Count()}</td></tr>");
            sb.Append("</tbody></table>");
        }
        return sb.ToString();
    }

    private static string RenderOffers(WatchItem item, ItemHistory? h)
    {
        if (h?.LastOffers is not { Count: > 0 } offers) return "";
        var sb = new StringBuilder();
        var when = h.LastOffersAt is { } at ? $" ({at.ToLocalTime():dd/MM/yyyy HH:mm})" : "";
        sb.Append($"<div class=\"offers-title\">Mejores ofertas en la última comprobación{when}</div>");
        sb.Append("<table class=\"offers\"><thead><tr><th>Precio</th><th>Tienda</th><th>Producto</th></tr></thead><tbody>");
        for (var i = 0; i < offers.Count; i++)
        {
            var o = offers[i];
            sb.Append($"<tr{(i == 0 ? " class=\"best\"" : "")}><td class=\"price\">{PriceParser.Format(o.Price, item.Currency)}</td>");
            sb.Append($"<td>{E(o.Store)}{(o.PickupMadrid ? " <span class=\"tag\">recogida en Madrid</span>" : "")}</td>");
            sb.Append($"<td><a href=\"{E(o.Url)}\">{E(o.Title)}</a></td></tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string Stat(string cls, string label, string value) =>
        $"<div class=\"stat {cls}\"><div class=\"label\">{label}</div><div class=\"value\">{value}</div></div>";

    private static string MonthLabel(string month)
    {
        var parts = month.Split('-');
        return int.TryParse(parts[1], out var m) && m is >= 1 and <= 12 ? $"{Months[m - 1]} {parts[0]}" : month;
    }

    private static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    public static void Write(string path, Watchlist watchlist, Dictionary<string, ItemHistory> history, DateTimeOffset generatedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Build(watchlist, history, generatedAt));
    }

    private const string Template = """
        <!doctype html>
        <html lang="es">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>OffersFinder – evolución de precios</title>
        <style>
          :root { --bg:#f6f7f9; --card:#fff; --text:#1f2933; --muted:#6b7280; --border:#e5e7eb; --min:#16a34a; --max:#dc2626; --mean:#f59e0b; --median:#7c3aed; }
          @media (prefers-color-scheme: dark) { :root { --bg:#0f172a; --card:#1e293b; --text:#e2e8f0; --muted:#94a3b8; --border:#334155; } }
          * { box-sizing:border-box; }
          body { margin:0; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; background:var(--bg); color:var(--text); }
          header { padding:16px; border-bottom:1px solid var(--border); background:var(--card); }
          header h1 { margin:0; font-size:20px; }
          .meta { color:var(--muted); font-size:13px; margin-top:4px; }
          main { max-width:1000px; margin:0 auto; padding:12px; display:grid; gap:14px; }
          .card { background:var(--card); border:1px solid var(--border); border-radius:12px; padding:14px; }
          .card h2 { margin:0 0 4px; font-size:17px; }
          .sub { color:var(--muted); font-size:13px; margin-bottom:8px; word-break:break-all; }
          .tag { display:inline-block; font-size:11px; padding:2px 8px; border-radius:999px; background:var(--bg); border:1px solid var(--border); margin:2px 4px 2px 0; }
          .tabs { display:flex; flex-wrap:wrap; gap:6px; margin:10px 0; }
          .tab-radio { position:absolute; opacity:0; pointer-events:none; }
          .tabs label { font-size:13px; padding:5px 10px; border:1px solid var(--border); border-radius:999px; cursor:pointer; color:var(--muted); }
          .panel { display:none; }
          .stats { display:grid; grid-template-columns:repeat(auto-fit,minmax(110px,1fr)); gap:8px; margin-bottom:10px; }
          .stat { border:1px solid var(--border); border-radius:10px; padding:8px 10px; }
          .stat .label { font-size:11px; color:var(--muted); }
          .stat .value { font-size:15px; font-weight:600; word-break:break-word; }
          .stat.min .value { color:var(--min); } .stat.max .value { color:var(--max); }
          .stat.mean .value { color:var(--mean); } .stat.median .value { color:var(--median); }
          svg.chart { width:100%; height:auto; display:block; }
          svg .grid { stroke:var(--border); stroke-width:1; }
          svg .tick { font-size:13px; fill:var(--muted); }
          svg .muted { fill:var(--muted); font-size:14px; }
          .legend { display:flex; flex-wrap:wrap; gap:10px; font-size:12px; color:var(--muted); margin:6px 0 10px; }
          .legend i { display:inline-block; width:10px; height:10px; border-radius:2px; margin-right:5px; vertical-align:middle; }
          table { width:100%; border-collapse:collapse; margin:6px 0; font-size:14px; }
          th, td { text-align:left; padding:6px 8px; border-bottom:1px solid var(--border); vertical-align:top; }
          th { font-size:12px; color:var(--muted); font-weight:600; }
          td.price { white-space:nowrap; font-weight:600; }
          tr.best td { background:rgba(22,163,74,0.08); }
          .offers-title { font-size:13px; color:var(--muted); margin-top:12px; }
          .empty { color:var(--muted); font-style:italic; padding:8px 0; }
          a { color:#2563eb; }
          __CSS__
        </style>
        </head>
        <body>
        __BODY__
        </body>
        </html>
        """;
}
