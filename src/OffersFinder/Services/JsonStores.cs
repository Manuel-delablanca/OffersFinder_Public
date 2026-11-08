using System.Text.Encodings.Web;
using System.Text.Json;
using OffersFinder.Models;

namespace OffersFinder.Services;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>Lee y guarda la lista de productos a vigilar (watchlist.json).</summary>
public sealed class WatchlistStore
{
    private readonly string _path;
    public WatchlistStore(string path) => _path = path;

    public Watchlist Load()
    {
        if (!File.Exists(_path)) return new Watchlist();
        return JsonSerializer.Deserialize<Watchlist>(File.ReadAllText(_path), Json.Options) ?? new Watchlist();
    }

    public void Save(Watchlist watchlist)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(watchlist, Json.Options) + Environment.NewLine);
    }
}

/// <summary>Lee el catálogo de tiendas (stores.json).</summary>
public sealed class StoreCatalogStore
{
    private readonly string _path;
    public StoreCatalogStore(string path) => _path = path;

    public StoreCatalog Load()
    {
        if (!File.Exists(_path)) return new StoreCatalog();
        return JsonSerializer.Deserialize<StoreCatalog>(File.ReadAllText(_path), Json.Options) ?? new StoreCatalog();
    }
}

/// <summary>Guarda el histórico de precios en un archivo JSON (fácil de versionar en git).</summary>
public sealed class HistoryStore
{
    private readonly string _path;
    public HistoryStore(string path) => _path = path;

    public Dictionary<string, ItemHistory> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, ItemHistory>();
        return JsonSerializer.Deserialize<Dictionary<string, ItemHistory>>(File.ReadAllText(_path), Json.Options)
               ?? new Dictionary<string, ItemHistory>();
    }

    public void Save(Dictionary<string, ItemHistory> history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(history, Json.Options) + Environment.NewLine);
    }
}
