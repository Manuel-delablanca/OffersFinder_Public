using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OffersFinder.Models;

namespace OffersFinder.Services;

/// <summary>
/// Comparación tolerante de títulos: sin mayúsculas ni acentos, sin espacios ni guiones entre letras y
/// números ("2 TB" = "2tb", "MX-12" = "mx12") y con sinónimos separados por "|" ("2tb|2000gb").
/// Título y términos se normalizan igual, de modo que la comparación por contenido sigue siendo válida.
/// </summary>
public static partial class TextMatch
{
    [GeneratedRegex(@"(?<=\d)[\s\-_]+(?=\p{L})|(?<=\p{L})[\s\-_]+(?=\d)")]
    private static partial Regex NumberLetterGap();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c);
        }
        var result = NumberLetterGap().Replace(sb.ToString(), "");
        return Spaces().Replace(result, " ").Trim();
    }

    /// <summary>Palabras requeridas del producto: mustContain o, si falta, las de la búsqueda.</summary>
    public static IReadOnlyList<string> RequiredTerms(WatchItem item) =>
        item.MustContain is { Count: > 0 }
            ? item.MustContain
            : (item.Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length > 1).ToList();

    /// <summary>Un término casa si alguno de sus sinónimos ("a|b|c") aparece en el texto normalizado.</summary>
    public static bool TermMatches(string normalizedText, string term) =>
        term.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(alt => alt.Length > 0)
            .Any(normalizedText.Contains);

    public static List<string> MissingTerms(string title, IEnumerable<string> terms)
    {
        var normalized = Normalize(title);
        return terms.Where(t => !TermMatches(normalized, t)).ToList();
    }

    public static string? FirstExcluded(string title, IEnumerable<string>? excluded)
    {
        if (excluded is null) return null;
        var normalized = Normalize(title);
        return excluded.FirstOrDefault(e => TermMatches(normalized, e));
    }

    public static bool ContainsAll(string title, IEnumerable<string> terms) => MissingTerms(title, terms).Count == 0;
}
