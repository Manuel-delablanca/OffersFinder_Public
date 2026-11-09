using System.Globalization;
using OffersFinder.Models;
using OffersFinder.Notifications;
using OffersFinder.Services;

namespace OffersFinder;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var settings = Settings.FromEnvironment();

        var command = args.Length == 0 ? "check" : args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "check" => await CheckAsync(settings, rest),
                "add" => Add(settings, rest),
                "remove" => Remove(settings, rest),
                "list" => List(settings),
                "stores" => Stores(settings),
                "history" => History(settings, rest),
                "report" => Report(settings, rest),
                "blacklist" => Blacklist(settings, rest, add: true),
                "unblacklist" => Blacklist(settings, rest, add: false),
                "test-notify" => await TestNotifyAsync(settings),
                "notify" => await NotifyAsync(settings, rest),
                "summary" => await SummaryAsync(settings, rest),
                "send-report" => await SendReportAsync(settings),
                "telegram-setup" => await TelegramSetupAsync(settings),
                "diagnose" => await DiagnoseAsync(settings, rest),
                "help" or "--help" or "-h" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CheckAsync(Settings settings, string[] args)
    {
        var loop = args.Contains("--loop");
        var every = ParseInterval(OptionValue(args, "--every")) ?? TimeSpan.FromHours(6);
        var dryRun = args.Contains("--dry-run");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        do
        {
            await RunOnceAsync(settings, dryRun, cts.Token);
            if (loop)
            {
                Console.WriteLine($"Próxima comprobación en {every} ({DateTime.Now.Add(every):g}). Ctrl+C para salir.");
                try { await Task.Delay(every, cts.Token); } catch (TaskCanceledException) { break; }
            }
        } while (loop && !cts.IsCancellationRequested);

        return 0;
    }

    private static async Task RunOnceAsync(Settings settings, bool dryRun, CancellationToken ct)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        if (watchlist.Items.Count == 0)
        {
            Console.WriteLine($"La lista {settings.WatchlistPath} está vacía. Añade productos con 'add' o edita el archivo.");
            return;
        }

        var stores = new StoreCatalogStore(settings.StoresPath).Load().Stores;
        var historyStore = new HistoryStore(settings.HistoryPath);
        var history = historyStore.Load();
        var notifiers = NotifierFactory.FromSettings(settings);
        var now = DateTimeOffset.UtcNow;

        var provider = !string.IsNullOrWhiteSpace(settings.SerperApiKey) ? "Serper" : "SerpApi";
        if (settings.ApiPaused)
            Console.WriteLine($"⏸ API de búsqueda en pausa hasta {settings.ApiPauseUntil:yyyy-MM-dd}: solo se leen las fichas de producto (URL).");
        else if (settings.ApiPauseUntil is { } until && settings.Today < until)
            Console.WriteLine($"▶ API en pausa hasta {until:yyyy-MM-dd}, pero hoy es día de excepción: se llama a la API.");
        var web = settings.UseShoppingSearch && settings.WebSearch ? " + Google web" : "";
        var source = settings.UseShoppingSearch && settings.UseStoreSearch ? $"Google Shopping{web} vía {provider} + tiendas"
                   : settings.UseShoppingSearch ? $"Google Shopping{web} (España) vía {provider}"
                   : settings.UseStoreSearch ? "tiendas del catálogo" : settings.ApiPaused ? "en pausa" : "ninguna";
        Console.WriteLine($"[{DateTime.Now:g}] Comprobando {watchlist.Items.Count} producto(s) " +
                          $"(avisos: {string.Join(", ", notifiers.Select(n => n.Name))}; búsquedas: {source})");

        using var fetcher = new PageFetcher(settings);
        var searcher = BuildSearcher(settings, fetcher, stores, watchlist);
        var checker = new PriceChecker(settings, fetcher, searcher, Console.WriteLine);

        var alerts = new List<Alert>();
        foreach (var item in watchlist.Items)
        {
            ct.ThrowIfCancellationRequested();
            Console.WriteLine($"→ {item.DisplayName}");
            var result = await checker.CheckAsync(item, history, now, ct);

            if (result.Price is null)
            {
                Console.WriteLine($"  ⚠️  {result.Error}");
                continue;
            }

            var previousText = result.Previous is null ? "primera vez" : $"antes {PriceParser.Format(result.Previous.Price, item.Currency)}";
            Console.WriteLine($"  {PriceParser.Format(result.Price.Value, item.Currency)} ({previousText}, {result.Offers.Count} oferta(s))");
            alerts.AddRange(result.Alerts);
        }

        // Los avisos se envían al final, agrupados: el email manda un solo correo por ejecución.
        if (alerts.Count > 0)
        {
            foreach (var notifier in notifiers)
            {
                if (dryRun && notifier is not ConsoleNotifier) continue;
                try { await notifier.SendManyAsync(alerts, ct); }
                catch (Exception ex) { Console.Error.WriteLine($"❌ No se pudo avisar por {notifier.Name}: {ex.Message}"); }
            }
        }

        if (!dryRun)
        {
            historyStore.Save(history);
            ReportGenerator.Write(settings.ReportPath, watchlist, history, now);
        }
        Console.WriteLine($"Hecho. {alerts.Count} aviso(s). Histórico en {settings.HistoryPath}, informe en {settings.ReportPath}{(dryRun ? " (no guardados: --dry-run)" : "")}.");
    }

    private static ShoppingSearchClient? BuildShoppingClient(Settings settings)
    {
        if (!settings.UseShoppingSearch) return null;
        return !string.IsNullOrWhiteSpace(settings.SerperApiKey)
            ? new ShoppingSearchClient(settings.SerperApiKey, endpoint: settings.SerperEndpoint, location: settings.SerperLocation, provider: ShoppingProvider.Serper)
            : new ShoppingSearchClient(settings.SerpApiKey!, endpoint: settings.SerperEndpoint, location: settings.SerperLocation, provider: ShoppingProvider.SerpApi);
    }

    private static OfferSearcher BuildSearcher(Settings settings, PageFetcher fetcher, List<Store> stores, Watchlist watchlist)
    {
        var shopping = BuildShoppingClient(settings);
        if (shopping is null && settings.SearchMode is "shopping")
            Console.WriteLine("SEARCH_MODE=shopping pero falta SERPER_API_KEY o SERPAPI_API_KEY: no se buscará en ninguna fuente.");
        var scanner = shopping is not null && settings.WebSearch
            ? new WebResultScanner(fetcher, stores, watchlist.BlacklistedStores, settings.WebSearchMaxPages)
            : null;
        return new OfferSearcher(fetcher, stores, watchlist.BlacklistedStores, Console.WriteLine, shopping, settings.UseStoreSearch, scanner);
    }

    private static int Add(Settings settings, string[] args)
    {
        var url = args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        var query = OptionValue(args, "--query");
        if (url is null && query is null)
        {
            Console.Error.WriteLine("Uso: add [URL] [--query \"texto\"] [--name \"Nombre\"] [--target 199.99] [--max 400] [--min 50] [--selector \".price\"] [--exclude funda,cable] [--stores amazon_es,pccomponentes] [--exclude-stores amazon_es] [--pickup-madrid] [--with-shipping] [--urls url1,url2]");
            return 2;
        }

        var store = new WatchlistStore(settings.WatchlistPath);
        var watchlist = store.Load();
        var item = new WatchItem
        {
            Name = OptionValue(args, "--name") ?? query ?? url!,
            Url = url,
            Urls = SplitList(OptionValue(args, "--urls")),
            Query = query,
            TargetPrice = ParseDecimal(OptionValue(args, "--target")),
            MaxPrice = ParseDecimal(OptionValue(args, "--max")),
            MinPrice = ParseDecimal(OptionValue(args, "--min")),
            Selector = OptionValue(args, "--selector"),
            Exclude = SplitList(OptionValue(args, "--exclude")),
            Stores = SplitList(OptionValue(args, "--stores")),
            ExcludeStores = SplitList(OptionValue(args, "--exclude-stores")),
            PickupMadridOnly = args.Contains("--pickup-madrid"),
            CompareWithShipping = args.Contains("--with-shipping"),
        };

        if (watchlist.Items.Any(i => i.Key == item.Key))
        {
            Console.WriteLine("Ese producto ya está en la lista.");
            return 0;
        }
        watchlist.Items.Add(item);
        store.Save(watchlist);
        Console.WriteLine($"Añadido: {item.DisplayName}");
        return 0;
    }

    private static int Remove(Settings settings, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Uso: remove <nombre|URL|número> [--keep-history]");
            return 2;
        }
        var store = new WatchlistStore(settings.WatchlistPath);
        var watchlist = store.Load();
        var needle = string.Join(' ', args.Where(a => !a.StartsWith("--")));
        var item = FindItem(watchlist, needle);
        if (item is null)
        {
            Console.Error.WriteLine("No se encontró ese producto.");
            return 1;
        }
        watchlist.Items.Remove(item);
        store.Save(watchlist);

        if (!args.Contains("--keep-history"))
        {
            var historyStore = new HistoryStore(settings.HistoryPath);
            var history = historyStore.Load();
            if (history.Remove(item.Key)) historyStore.Save(history);
        }
        Console.WriteLine($"Eliminado: {item.DisplayName}{(args.Contains("--keep-history") ? " (histórico conservado)" : " (y su histórico)")}");
        return 0;
    }

    private static int List(Settings settings)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var history = new HistoryStore(settings.HistoryPath).Load();
        if (watchlist.Items.Count == 0)
        {
            Console.WriteLine("La lista está vacía.");
            return 0;
        }
        if (watchlist.BlacklistedStores.Count > 0)
            Console.WriteLine($"Lista negra global: {string.Join(", ", watchlist.BlacklistedStores)}");
        var n = 0;
        foreach (var item in watchlist.Items)
        {
            history.TryGetValue(item.Key, out var h);
            var last = h?.Last;
            var lastText = last is null ? "sin datos" : $"{PriceParser.Format(last.Price, item.Currency)} ({last.Timestamp.ToLocalTime():g}{(last.Store is null ? "" : $", {last.Store}")})";
            var lowest = h?.LowestEver is { } low ? $", mínimo {PriceParser.Format(low, item.Currency)}" : "";
            var target = item.TargetPrice is { } t ? $", objetivo {PriceParser.Format(t, item.Currency)}" : "";
            Console.WriteLine($"{++n}. {item.DisplayName}");
            if (item.HasUrl) Console.WriteLine($"   URL: {item.Url}");
            if (item.HasQuery) Console.WriteLine($"   Búsqueda: \"{item.Query}\"{(item.PickupMadridOnly ? " (solo recogida en Madrid)" : "")}");
            if (item.ExcludeStores is { Count: > 0 }) Console.WriteLine($"   Tiendas excluidas: {string.Join(", ", item.ExcludeStores)}");
            Console.WriteLine($"   Último: {lastText}{lowest}{target}");
        }
        return 0;
    }

    private static int Stores(Settings settings)
    {
        var stores = new StoreCatalogStore(settings.StoresPath).Load().Stores;
        var blacklist = new WatchlistStore(settings.WatchlistPath).Load().BlacklistedStores;
        if (stores.Count == 0)
        {
            Console.WriteLine($"No hay tiendas en {settings.StoresPath}.");
            return 0;
        }
        foreach (var s in stores)
        {
            var flags = new List<string>();
            if (!s.Enabled) flags.Add("desactivada");
            if (blacklist.Contains(s.Id, StringComparer.OrdinalIgnoreCase)) flags.Add("⛔ lista negra");
            if (s.ShipsToSpain) flags.Add("envía a España");
            if (s.PickupMadrid) flags.Add("recogida en Madrid");
            Console.WriteLine($"- {s.Id,-16} {s.Name,-22} {string.Join(", ", flags)}");
            if (s.PickupNote is not null) Console.WriteLine($"  {s.PickupNote}");
        }
        return 0;
    }

    private static int History(Settings settings, string[] args)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var history = new HistoryStore(settings.HistoryPath).Load();
        var month = OptionValue(args, "--month");
        var showAll = args.Contains("--all");
        var needle = string.Join(' ', args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--month")));
        var items = string.IsNullOrWhiteSpace(needle)
            ? watchlist.Items
            : watchlist.Items.Where(i => i.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var item in items)
        {
            Console.WriteLine($"== {item.DisplayName}");
            history.TryGetValue(item.Key, out var h);
            var records = Statistics.FilterByMonth(h?.Records ?? new List<PriceRecord>(), month).ToList();
            var stats = Statistics.Compute(records);
            if (stats is null)
            {
                Console.WriteLine(month is null ? "   (sin registros)" : $"   (sin registros en {month})");
                if (h is { Records.Count: > 0 })
                    Console.WriteLine($"   Meses con datos: {string.Join(", ", Statistics.AvailableMonths(h.Records))}");
                continue;
            }

            var c = item.Currency;
            Console.WriteLine($"   Consultas: {stats.Count}{(month is null ? "" : $" (mes {month})")}   " +
                              $"Mínimo: {PriceParser.Format(stats.Min, c)}   Máximo: {PriceParser.Format(stats.Max, c)}   " +
                              $"Media: {PriceParser.Format(stats.Mean, c)}   Mediana: {PriceParser.Format(stats.Median, c)}");
            Console.WriteLine($"   Primero: {PriceParser.Format(stats.First.Price, c)} ({stats.First.Timestamp.ToLocalTime():g})   " +
                              $"Último: {PriceParser.Format(stats.Last.Price, c)} ({stats.Last.Timestamp.ToLocalTime():g})   " +
                              $"Variación: {(stats.Change >= 0 ? "+" : "")}{PriceParser.Format(stats.Change, c)}");
            if (month is null)
                Console.WriteLine($"   Meses con datos: {string.Join(", ", Statistics.AvailableMonths(records))}  (filtra con --month AAAA-MM)");

            var storeLatest = (h?.StoreRecords ?? new List<StoreRecord>())
                .Where(r => month is null || Statistics.FilterByMonth(new[] { new PriceRecord { Timestamp = r.Timestamp, Price = r.Price } }, month).Any())
                .GroupBy(r => r.Store)
                .Select(g => (Store: g.Key, Last: g.OrderByDescending(r => r.Timestamp).First().Price, Min: g.Min(r => r.Price), Count: g.Count()))
                .OrderBy(x => x.Last).ToList();
            if (storeLatest.Count > 0)
            {
                Console.WriteLine("   Por tienda (último / mínimo / consultas):");
                foreach (var st in storeLatest)
                    Console.WriteLine($"     {st.Store,-28} {PriceParser.Format(st.Last, c),12} {PriceParser.Format(st.Min, c),12}   {st.Count}");
            }

            var shown = showAll ? records : records.TakeLast(30).ToList();
            if (!showAll && records.Count > 30) Console.WriteLine($"   ... mostrando los últimos 30 de {records.Count} (usa --all)");
            foreach (var r in shown)
            {
                var marker = r.Price == stats.Min ? " ▼ mín" : r.Price == stats.Max ? " ▲ máx" : "";
                Console.WriteLine($"   {r.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}  {PriceParser.Format(r.Price, c),12}  {r.Store ?? "",-18}{marker}");
            }
        }
        Console.WriteLine($"Gráfica: ejecuta 'report' y abre {settings.ReportPath}.");
        return 0;
    }

    private static int Report(Settings settings, string[] args)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var history = new HistoryStore(settings.HistoryPath).Load();
        var path = OptionValue(args, "--output") ?? settings.ReportPath;
        ReportGenerator.Write(path, watchlist, history, DateTimeOffset.UtcNow);
        Console.WriteLine($"Informe generado: {Path.GetFullPath(path)}");

        if (args.Contains("--open"))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"No se pudo abrir el navegador: {ex.Message}");
            }
        }
        return 0;
    }

    private static int Blacklist(Settings settings, string[] args, bool add)
    {
        var verb = add ? "blacklist" : "unblacklist";
        var storeId = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (storeId is null)
        {
            Console.Error.WriteLine($"Uso: {verb} <idTienda> [--item <nombre|URL|número>]   (sin --item se aplica a todos los productos)");
            Console.Error.WriteLine("Identificadores de tienda: ejecuta 'stores'.");
            return 2;
        }

        var stores = new StoreCatalogStore(settings.StoresPath).Load().Stores;
        var store = stores.FirstOrDefault(s => string.Equals(s.Id, storeId, StringComparison.OrdinalIgnoreCase));
        if (store is null)
        {
            Console.Error.WriteLine($"No existe la tienda '{storeId}' en {settings.StoresPath}. Identificadores válidos: {string.Join(", ", stores.Select(s => s.Id))}");
            return 1;
        }

        var watchlistStore = new WatchlistStore(settings.WatchlistPath);
        var watchlist = watchlistStore.Load();
        var itemNeedle = OptionValue(args, "--item");

        if (itemNeedle is null)
        {
            var changed = add
                ? !watchlist.BlacklistedStores.Contains(store.Id, StringComparer.OrdinalIgnoreCase) && AddTo(watchlist.BlacklistedStores, store.Id)
                : watchlist.BlacklistedStores.RemoveAll(s => string.Equals(s, store.Id, StringComparison.OrdinalIgnoreCase)) > 0;
            watchlistStore.Save(watchlist);
            Console.WriteLine(add
                ? $"{store.Name} en la lista negra global: no se volverá a consultar para ningún producto."
                : changed ? $"{store.Name} eliminada de la lista negra global." : $"{store.Name} no estaba en la lista negra global.");
            return 0;
        }

        var item = FindItem(watchlist, itemNeedle);
        if (item is null)
        {
            Console.Error.WriteLine($"No se encontró el producto '{itemNeedle}'.");
            return 1;
        }
        item.ExcludeStores ??= new List<string>();
        if (add)
        {
            if (!item.ExcludeStores.Contains(store.Id, StringComparer.OrdinalIgnoreCase)) item.ExcludeStores.Add(store.Id);
            Console.WriteLine($"{store.Name} no se volverá a consultar para \"{item.DisplayName}\".");
        }
        else
        {
            item.ExcludeStores.RemoveAll(s => string.Equals(s, store.Id, StringComparison.OrdinalIgnoreCase));
            if (item.ExcludeStores.Count == 0) item.ExcludeStores = null;
            Console.WriteLine($"{store.Name} vuelve a consultarse para \"{item.DisplayName}\".");
        }
        watchlistStore.Save(watchlist);
        return 0;
    }

    private static bool AddTo(List<string> list, string value)
    {
        list.Add(value);
        return true;
    }

    private static WatchItem? FindItem(Watchlist watchlist, string needle)
    {
        needle = needle.Trim();
        if (int.TryParse(needle, out var index) && index >= 1 && index <= watchlist.Items.Count)
            return watchlist.Items[index - 1];
        return watchlist.Items.FirstOrDefault(i =>
                   string.Equals(i.Name, needle, StringComparison.OrdinalIgnoreCase) || i.Url == needle || i.Query == needle)
               ?? watchlist.Items.FirstOrDefault(i => i.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> TestNotifyAsync(Settings settings)
    {
        var notifiers = NotifierFactory.FromSettings(settings);
        var alert = new Alert("✅ OffersFinder funciona", "Este es un aviso de prueba. Si lo lees, las notificaciones están bien configuradas.", null);
        foreach (var notifier in notifiers)
        {
            try
            {
                await notifier.SendAsync(alert);
                Console.WriteLine($"Enviado por {notifier.Name}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fallo en {notifier.Name}: {ex.Message}");
            }
        }
        if (notifiers.Count == 1)
            Console.WriteLine("Solo está activa la consola. Configura TELEGRAM_BOT_TOKEN y TELEGRAM_CHAT_ID en .env (ejecuta 'telegram-setup' para obtener el chat_id).");
        return 0;
    }

    private static async Task<int> DiagnoseAsync(Settings settings, string[] args)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var stores = new StoreCatalogStore(settings.StoresPath).Load().Stores;
        var needle = string.Join(' ', args);
        var items = string.IsNullOrWhiteSpace(needle)
            ? watchlist.Items
            : watchlist.Items.Where(i => i.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0)
        {
            Console.WriteLine("No hay productos que diagnosticar.");
            return 0;
        }

        using var fetcher = new PageFetcher(settings, retries: 1);
        var searcher = BuildSearcher(settings, fetcher, stores, watchlist);
        var shopping = BuildShoppingClient(settings);
        var diagnostics = new Diagnostics(fetcher, searcher, Console.WriteLine, shopping, settings.UseStoreSearch, settings.WebSearch, settings.ApiPaused);
        foreach (var item in items)
            await diagnostics.RunAsync(item);
        return 0;
    }

    /// <summary>Resumen de los últimos N días (7 por defecto) por los canales configurados.</summary>
    private static async Task<int> SummaryAsync(Settings settings, string[] args)
    {
        var days = int.TryParse(OptionValue(args, "--days"), out var d) && d > 0 ? d : 7;
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var history = new HistoryStore(settings.HistoryPath).Load();
        var body = SummaryBuilder.Build(watchlist, history, days, DateTimeOffset.UtcNow);
        var alert = new Alert($"📊 Resumen de precios ({days} días)", body, null);
        var failures = 0;
        foreach (var notifier in NotifierFactory.FromSettings(settings))
        {
            try { await notifier.SendAsync(alert); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Fallo en {notifier.Name}: {ex.Message}"); }
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Genera el informe HTML y lo envía como archivo por los canales que lo admiten (Telegram, email).</summary>
    private static async Task<int> SendReportAsync(Settings settings)
    {
        var watchlist = new WatchlistStore(settings.WatchlistPath).Load();
        var history = new HistoryStore(settings.HistoryPath).Load();
        ReportGenerator.Write(settings.ReportPath, watchlist, history, DateTimeOffset.UtcNow);
        var notifiers = NotifierFactory.FromSettings(settings);
        var failures = 0;

        // Una imagen por producto con su gráfica (lo que mejor se ve en el móvil)...
        foreach (var item in watchlist.Items)
        {
            history.TryGetValue(item.Key, out var h);
            var stats = Statistics.Compute(h?.Records ?? new List<PriceRecord>());
            if (stats is null) continue;
            var c = item.Currency;
            var caption = $"📈 {item.DisplayName}\nÚltimo: {PriceParser.Format(stats.Last.Price, c)}{(stats.Last.Store is null ? "" : $" ({stats.Last.Store})")}\n" +
                          $"Mínimo: {PriceParser.Format(stats.Min, c)} · Máximo: {PriceParser.Format(stats.Max, c)} · Media: {PriceParser.Format(stats.Mean, c)} · Mediana: {PriceParser.Format(stats.Median, c)}" +
                          (item.TargetPrice is { } t ? $"\nObjetivo: {PriceParser.Format(t, c)}" : "");
            byte[] png;
            try { png = SkiaChart.RenderPng(item.DisplayName, ReportGenerator.ChartSeriesFor(item, h), item.TargetPrice, c); }
            catch (Exception ex) { Console.Error.WriteLine($"No se pudo generar la imagen de {item.DisplayName}: {ex.Message}"); continue; }
            var fileName = $"{Store.Normalize(item.DisplayName)}.png";
            foreach (var notifier in notifiers)
            {
                try { await notifier.SendImageAsync(png, fileName, caption); }
                catch (Exception ex) { failures++; Console.Error.WriteLine($"Fallo en {notifier.Name}: {ex.Message}"); }
            }
        }

        // ...y el informe completo como archivo.
        var fileCaption = $"📊 Informe completo OffersFinder ({DateTime.Now:d}): gráficas por mes, estadísticas y tiendas.";
        foreach (var notifier in notifiers)
        {
            try { await notifier.SendFileAsync(settings.ReportPath, fileCaption); Console.WriteLine($"Informe enviado por {notifier.Name}."); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Fallo en {notifier.Name}: {ex.Message}"); }
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Envía un mensaje cualquiera por los canales configurados: notify "Título" "Texto".</summary>
    private static async Task<int> NotifyAsync(Settings settings, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Uso: notify \"Título\" \"Texto del mensaje\"");
            return 2;
        }
        var title = args[0];
        var body = args.Length > 1 ? string.Join(' ', args.Skip(1)).Replace("\\n", "\n") : "";
        var alert = new Alert(title, body, null);
        var failures = 0;
        foreach (var notifier in NotifierFactory.FromSettings(settings))
        {
            try { await notifier.SendAsync(alert); Console.WriteLine($"Enviado por {notifier.Name}."); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"Fallo en {notifier.Name}: {ex.Message}"); }
        }
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> TelegramSetupAsync(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken))
        {
            Console.Error.WriteLine("Falta TELEGRAM_BOT_TOKEN en .env. Crea el bot con @BotFather en Telegram y pega aquí el token que te da.");
            return 2;
        }

        Console.WriteLine("Consultando a Telegram los chats que han escrito al bot...");
        var chats = await TelegramNotifier.DiscoverChatsAsync(settings.TelegramBotToken);
        if (chats.Count == 0)
        {
            Console.WriteLine("Todavía nadie ha escrito al bot. Ábrelo en Telegram, pulsa 'Iniciar' (o envíale cualquier mensaje) y vuelve a ejecutar este comando.");
            return 1;
        }

        Console.WriteLine("Chats encontrados:");
        foreach (var chat in chats)
            Console.WriteLine($"  TELEGRAM_CHAT_ID={chat.Id}   ({chat.Description})");
        Console.WriteLine();
        Console.WriteLine(chats.Count == 1
            ? $"Pon TELEGRAM_CHAT_ID={chats[0].Id} en .env (y como secreto en GitHub) y ejecuta 'test-notify' para comprobarlo."
            : "Elige el chat que corresponde a tu usuario y ponlo como TELEGRAM_CHAT_ID en .env (y como secreto en GitHub).");
        return 0;
    }

    private static int Help()
    {
        Console.WriteLine("""
            OffersFinder – vigila precios y avisa cuando bajan.

            Comandos:
              check [--loop] [--every 6h] [--dry-run]   Comprueba todos los productos (por defecto). --loop repite cada intervalo.
              add URL | --query "texto" [opciones]       Añade un producto a la lista.
                  --name "Nombre"  --target 199.99  --max 400  --min 50  --selector ".price"
                  --exclude funda,cable  --stores amazon_es,pccomponentes  --pickup-madrid
              remove <nombre|URL|número> [--keep-history] Elimina un producto (y su histórico, salvo --keep-history).
              list                                       Muestra la lista con el último precio.
              stores                                     Muestra el catálogo de tiendas (envío a España / recogida en Madrid).
              history [nombre] [--month AAAA-MM] [--all]  Histórico con mínimo, máximo, media y mediana; filtrable por mes.
              report [--open] [--output ruta.html]       Genera el informe HTML con gráficas por producto (filtro por mes).
              blacklist <idTienda> [--item <producto>]   No volver a consultar esa tienda (para un producto o para todos).
              unblacklist <idTienda> [--item <producto>] Quita la tienda de la lista negra.
              test-notify                                Envía un aviso de prueba a los canales configurados.
              notify "Título" "Texto"                    Envía un mensaje cualquiera por los canales configurados.
              summary [--days 7]                         Envía el resumen de los últimos días (precio actual, variación, tiendas).
              send-report                                Genera el informe HTML y lo envía como archivo (Telegram, email).
              telegram-setup                             Muestra tu TELEGRAM_CHAT_ID a partir del token del bot.
              diagnose [nombre]                          Muestra qué ve el programa en cada tienda (para ajustar selectores).

            Configuración en .env (ver .env.example): Telegram (recomendado), email SMTP, WhatsApp (CallMeBot), MIN_DROP_PERCENT, rutas.
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: {command}");
        Help();
        return 2;
    }

    private static string? OptionValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static decimal? ParseDecimal(string? value) =>
        value is not null && decimal.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static List<string>? SplitList(string? value) =>
        value is null ? null : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static TimeSpan? ParseInterval(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().ToLowerInvariant();
        var unit = value[^1];
        var numberText = char.IsDigit(unit) ? value : value[..^1];
        if (!double.TryParse(numberText, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)) return null;
        return unit switch
        {
            's' => TimeSpan.FromSeconds(number),
            'm' => TimeSpan.FromMinutes(number),
            'h' => TimeSpan.FromHours(number),
            'd' => TimeSpan.FromDays(number),
            _ => TimeSpan.FromMinutes(number),
        };
    }
}
