using System.Text.Encodings.Web;
using System.Text.Json;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>Genera un informe HTML autocontenido con una gráfica y estadísticas por producto.</summary>
public static class ReportGenerator
{
    private static readonly JsonSerializerOptions DataOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(Watchlist watchlist, Dictionary<string, ItemHistory> history, DateTimeOffset generatedAt)
    {
        var items = watchlist.Items.Select(item =>
        {
            history.TryGetValue(item.Key, out var h);
            var records = (h?.Records ?? new List<PriceRecord>()).OrderBy(r => r.Timestamp).ToList();
            return new
            {
                key = item.Key,
                name = item.DisplayName,
                url = item.Url,
                query = item.Query,
                currency = item.Currency,
                targetPrice = item.TargetPrice,
                pickupMadridOnly = item.PickupMadridOnly,
                excludeStores = item.ExcludeStores ?? new List<string>(),
                records = records.Select(r => new { t = r.Timestamp.ToString("o"), p = r.Price, store = r.Store, url = r.Url, title = r.Title }),
                lastOffersAt = h?.LastOffersAt?.ToString("o"),
                lastOffers = (h?.LastOffers ?? new List<OfferRecord>()).Select(o => new { store = o.Store, price = o.Price, title = o.Title, url = o.Url, pickup = o.PickupMadrid }),
            };
        });

        var data = JsonSerializer.Serialize(new
        {
            generatedAt = generatedAt.ToString("o"),
            blacklistedStores = watchlist.BlacklistedStores,
            items,
        }, DataOptions);

        // El JSON se incrusta dentro de <script>; evitamos cerrar la etiqueta por accidente.
        data = data.Replace("</", "<\\/");

        return Template.Replace("__DATA__", data);
    }

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
        <script src="https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.4.1/chart.umd.js"></script>
        <style>
          :root { --bg:#f6f7f9; --card:#fff; --text:#1f2933; --muted:#6b7280; --line:#2563eb; --min:#16a34a; --max:#dc2626; --mean:#f59e0b; --median:#7c3aed; --border:#e5e7eb; }
          @media (prefers-color-scheme: dark) { :root { --bg:#0f172a; --card:#1e293b; --text:#e2e8f0; --muted:#94a3b8; --border:#334155; } }
          * { box-sizing:border-box; }
          body { margin:0; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; background:var(--bg); color:var(--text); }
          header { padding:20px 16px; border-bottom:1px solid var(--border); background:var(--card); display:flex; flex-wrap:wrap; gap:12px; align-items:center; justify-content:space-between; }
          header h1 { margin:0; font-size:20px; }
          header .meta { color:var(--muted); font-size:13px; }
          main { max-width:1100px; margin:0 auto; padding:16px; display:grid; gap:16px; }
          .card { background:var(--card); border:1px solid var(--border); border-radius:12px; padding:16px; }
          .card h2 { margin:0 0 4px; font-size:17px; }
          .card .sub { color:var(--muted); font-size:13px; margin-bottom:12px; word-break:break-all; }
          .toolbar { display:flex; flex-wrap:wrap; gap:8px; align-items:center; margin-bottom:12px; }
          select { padding:6px 8px; border-radius:8px; border:1px solid var(--border); background:var(--bg); color:var(--text); }
          .stats { display:grid; grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); gap:8px; margin-bottom:12px; }
          .stat { border:1px solid var(--border); border-radius:10px; padding:10px; }
          .stat .label { font-size:12px; color:var(--muted); }
          .stat .value { font-size:18px; font-weight:600; }
          .stat.min .value { color:var(--min); } .stat.max .value { color:var(--max); }
          .stat.mean .value { color:var(--mean); } .stat.median .value { color:var(--median); }
          .chart { position:relative; height:280px; }
          .empty { color:var(--muted); font-style:italic; }
          .tag { display:inline-block; font-size:11px; padding:2px 8px; border-radius:999px; background:var(--bg); border:1px solid var(--border); margin-right:4px; }
          table.offers { width:100%; border-collapse:collapse; margin:12px 0 4px; font-size:14px; }
          table.offers th, table.offers td { text-align:left; padding:6px 8px; border-bottom:1px solid var(--border); vertical-align:top; }
          table.offers th { font-size:12px; color:var(--muted); font-weight:600; }
          table.offers td.price { white-space:nowrap; font-weight:600; }
          table.offers tr.best td { background:rgba(22,163,74,0.08); }
          .offers-title { font-size:13px; color:var(--muted); margin-top:14px; }
          a { color:var(--line); }
        </style>
        </head>
        <body>
        <header>
          <div>
            <h1>OffersFinder – evolución de precios</h1>
            <div class="meta" id="generated"></div>
          </div>
          <div class="toolbar">
            <label for="globalMonth">Mes:</label>
            <select id="globalMonth"><option value="">Todos</option></select>
          </div>
        </header>
        <main id="items"></main>
        <script id="data" type="application/json">__DATA__</script>
        <script>
        (function () {
          const data = JSON.parse(document.getElementById('data').textContent);
          const fmtDate = new Intl.DateTimeFormat('es-ES', { dateStyle: 'short', timeStyle: 'short' });
          const fmtMonth = new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' });
          const money = (v, cur) => cur === '€'
            ? new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' }).format(v)
            : cur + Number(v).toFixed(2);
          const monthKey = d => d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0');

          document.getElementById('generated').textContent =
            'Generado el ' + fmtDate.format(new Date(data.generatedAt)) +
            (data.blacklistedStores.length ? ' · Lista negra global: ' + data.blacklistedStores.join(', ') : '');

          function stats(records) {
            if (!records.length) return null;
            const prices = records.map(r => r.p).sort((a, b) => a - b);
            const mid = Math.floor(prices.length / 2);
            const median = prices.length % 2 ? prices[mid] : (prices[mid - 1] + prices[mid]) / 2;
            const mean = prices.reduce((a, b) => a + b, 0) / prices.length;
            return { count: prices.length, min: prices[0], max: prices[prices.length - 1], mean, median,
                     first: records[0], last: records[records.length - 1] };
          }

          const allMonths = new Set();
          data.items.forEach(it => it.records.forEach(r => allMonths.add(monthKey(new Date(r.t)))));
          const globalSelect = document.getElementById('globalMonth');
          [...allMonths].sort().forEach(m => {
            const o = document.createElement('option'); o.value = m; o.textContent = fmtMonth.format(new Date(m + '-01T00:00:00'));
            globalSelect.appendChild(o);
          });

          const charts = new Map();
          const main = document.getElementById('items');

          data.items.forEach((item, idx) => {
            const card = document.createElement('section'); card.className = 'card';
            const months = [...new Set(item.records.map(r => monthKey(new Date(r.t))))].sort();
            const tags = [];
            if (item.pickupMadridOnly) tags.push('solo recogida en Madrid');
            if (item.targetPrice != null) tags.push('objetivo ' + money(item.targetPrice, item.currency));
            if (item.excludeStores.length) tags.push('excluye: ' + item.excludeStores.join(', '));
            card.innerHTML = `
              <h2>${esc(item.name)}</h2>
              <div class="sub">${item.url ? '<a href="' + esc(item.url) + '" target="_blank" rel="noopener">' + esc(item.url) + '</a>' : ''}
                ${item.query ? 'Búsqueda: “' + esc(item.query) + '”' : ''}</div>
              <div>${tags.map(t => '<span class="tag">' + esc(t) + '</span>').join('')}</div>
              <div class="toolbar"><label>Mes: <select class="month"><option value="">Todos</option>${months.map(m => '<option value="' + m + '">' + fmtMonth.format(new Date(m + '-01T00:00:00')) + '</option>').join('')}</select></label></div>
              <div class="stats"></div>
              <div class="chart"><canvas></canvas></div>
              ${offersTable(item)}`;
            main.appendChild(card);

            const select = card.querySelector('select.month');
            const statsBox = card.querySelector('.stats');
            const canvas = card.querySelector('canvas');

            function render(month) {
              const records = item.records.filter(r => !month || monthKey(new Date(r.t)) === month);
              const s = stats(records);
              if (!s) {
                statsBox.innerHTML = '<div class="empty">Sin registros' + (month ? ' en este mes' : ' todavía') + '.</div>';
                if (charts.has(idx)) { charts.get(idx).destroy(); charts.delete(idx); }
                return;
              }
              const change = s.last.p - s.first.p;
              statsBox.innerHTML = [
                ['min', 'Mínimo', money(s.min, item.currency)],
                ['max', 'Máximo', money(s.max, item.currency)],
                ['mean', 'Media', money(s.mean, item.currency)],
                ['median', 'Mediana', money(s.median, item.currency)],
                ['', 'Último', money(s.last.p, item.currency) + (s.last.store ? ' · ' + esc(s.last.store) : '')],
                ['', 'Variación', (change > 0 ? '+' : '') + money(change, item.currency) + ' en ' + s.count + ' consultas'],
              ].map(([cls, label, value]) => `<div class="stat ${cls}"><div class="label">${label}</div><div class="value">${value}</div></div>`).join('');

              const labels = records.map(r => fmtDate.format(new Date(r.t)));
              const line = (label, value, color) => ({ label, data: records.map(() => value), borderColor: color, borderDash: [6, 4], borderWidth: 1, pointRadius: 0, fill: false });
              const datasets = [
                { label: 'Precio', data: records.map(r => r.p), borderColor: getComputedStyle(document.documentElement).getPropertyValue('--line'), backgroundColor: 'rgba(37,99,235,0.12)', tension: 0.25, fill: true, pointRadius: 3 },
                line('Mínimo', s.min, '#16a34a'), line('Máximo', s.max, '#dc2626'), line('Media', s.mean, '#f59e0b'), line('Mediana', s.median, '#7c3aed'),
              ];
              if (item.targetPrice != null) datasets.push(line('Objetivo', item.targetPrice, '#0891b2'));

              if (charts.has(idx)) charts.get(idx).destroy();
              charts.set(idx, new Chart(canvas, {
                type: 'line',
                data: { labels, datasets },
                options: {
                  responsive: true, maintainAspectRatio: false, interaction: { mode: 'index', intersect: false },
                  plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } },
                    tooltip: { callbacks: { label: ctx => ctx.dataset.label + ': ' + money(ctx.parsed.y, item.currency) +
                      (ctx.datasetIndex === 0 && records[ctx.dataIndex].store ? ' (' + records[ctx.dataIndex].store + ')' : '') } } },
                  scales: { y: { ticks: { callback: v => money(v, item.currency) } } },
                }
              }));
            }

            select.addEventListener('change', () => render(select.value));
            globalSelect.addEventListener('change', () => {
              const m = globalSelect.value;
              select.value = months.includes(m) ? m : '';
              render(select.value);
            });
            render('');
          });

          function offersTable(item) {
            if (!item.lastOffers || !item.lastOffers.length) return '';
            const when = item.lastOffersAt ? fmtDate.format(new Date(item.lastOffersAt)) : '';
            const rows = item.lastOffers.map((o, i) => `<tr class="${i === 0 ? 'best' : ''}">
                <td class="price">${money(o.price, item.currency)}</td>
                <td>${esc(o.store)}${o.pickup ? ' <span class="tag">recogida en Madrid</span>' : ''}</td>
                <td><a href="${esc(o.url)}" target="_blank" rel="noopener">${esc(o.title)}</a></td></tr>`).join('');
            return `<div class="offers-title">Mejores ofertas en la última comprobación (${when})</div>
              <table class="offers"><thead><tr><th>Precio</th><th>Tienda</th><th>Producto</th></tr></thead><tbody>${rows}</tbody></table>`;
          }

          function esc(s) { return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
          if (!data.items.length) main.innerHTML = '<div class="card empty">No hay productos en la lista.</div>';
        })();
        </script>
        </body>
        </html>
        """;
}
