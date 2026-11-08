using System.Globalization;
using System.Text.RegularExpressions;

namespace OffersFinder.Services;

/// <summary>Convierte textos como "1.234,56 €", "$1,234.56" o "1234.5" en un número.</summary>
public static partial class PriceParser
{
    [GeneratedRegex(@"\d[\d.,\s ]*\d|\d")]
    private static partial Regex NumberPattern();

    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (Match match in NumberPattern().Matches(text))
        {
            var number = match.Value.Replace(" ", "").Replace(" ", "").Trim('.', ',');
            if (number.Length == 0) continue;

            var canonical = Canonicalize(number);
            if (decimal.TryParse(canonical, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
        }
        return null;
    }

    /// <summary>Decide qué separador es decimal y cuál de miles, y devuelve el número con punto decimal.</summary>
    private static string Canonicalize(string number)
    {
        var commas = number.Count(c => c == ',');
        var dots = number.Count(c => c == '.');

        if (commas > 0 && dots > 0)
        {
            // El separador que aparece en último lugar es el decimal.
            return number.LastIndexOf(',') > number.LastIndexOf('.')
                ? number.Replace(".", "").Replace(',', '.')
                : number.Replace(",", "");
        }

        if (commas + dots == 0) return number;

        var separator = commas > 0 ? ',' : '.';
        if (commas + dots > 1)
            return number.Replace(separator.ToString(), ""); // "1.234.567" -> miles

        var digitsAfter = number.Length - number.LastIndexOf(separator) - 1;
        // Tres cifras tras el único separador: casi seguro separador de miles ("1.299", "1,299").
        if (digitsAfter == 3)
            return number.Replace(separator.ToString(), "");

        return number.Replace(',', '.');
    }

    /// <summary>Formatea al estilo español ("1.234,56 €") sin depender de las culturas instaladas.</summary>
    public static string Format(decimal price, string currency = "€")
    {
        var negative = price < 0;
        var rounded = Math.Round(Math.Abs(price), 2, MidpointRounding.AwayFromZero);
        var whole = decimal.Truncate(rounded);
        var cents = (int)((rounded - whole) * 100);

        var digits = whole.ToString(CultureInfo.InvariantCulture);
        var grouped = new System.Text.StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) grouped.Append('.');
            grouped.Append(digits[i]);
        }

        var number = $"{(negative ? "-" : "")}{grouped},{cents:00}";
        return currency == "€" ? $"{number} €" : $"{currency}{number}";
    }
}
