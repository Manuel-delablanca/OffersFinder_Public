using System.Text.Json.Serialization;

namespace OffersFinder.Models;

public sealed class PriceRecord
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    /// <summary>Tienda donde se encontró el precio (en búsquedas).</summary>
    [JsonPropertyName("store")]
    public string? Store { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>Histórico de precios de un producto.</summary>
public sealed class ItemHistory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("records")]
    public List<PriceRecord> Records { get; set; } = new();

    /// <summary>Último precio por el que se envió un aviso de "precio objetivo".</summary>
    [JsonPropertyName("lastTargetAlertPrice")]
    public decimal? LastTargetAlertPrice { get; set; }

    /// <summary>Mejores ofertas encontradas en la última comprobación (para el informe).</summary>
    [JsonPropertyName("lastOffers")]
    public List<OfferRecord>? LastOffers { get; set; }

    [JsonPropertyName("lastOffersAt")]
    public DateTimeOffset? LastOffersAt { get; set; }

    [JsonIgnore]
    public PriceRecord? Last => Records.Count == 0 ? null : Records[^1];

    [JsonIgnore]
    public decimal? LowestEver => Records.Count == 0 ? null : Records.Min(r => r.Price);
}

public sealed class OfferRecord
{
    [JsonPropertyName("store")]
    public string Store { get; set; } = "";

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("pickupMadrid")]
    public bool PickupMadrid { get; set; }
}
