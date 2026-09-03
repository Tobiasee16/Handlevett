using System.Globalization;

namespace Handlevett.Services;

/// <summary>
/// Works out a package weight in grams from a product name, e.g. "Solvinge Kyllingfilet 700 g".
/// <para>
/// Extracted from the Kassal.app client so it can be tested directly. It guesses, and a wrong
/// guess silently distorts every price-per-gram calculation downstream, which is exactly why it
/// needs test coverage rather than a plausible-looking result nobody checks.
/// </para>
/// </summary>
public static class PackageSizeParser
{
    /// <summary>Used when a name carries no recognisable weight at all.</summary>
    public const int FallbackGrams = 500;

    private const int MaxReasonableGrams = 25_000;

    /// <summary>
    /// Returns the package weight in grams, or <see cref="FallbackGrams"/> when the name has none.
    /// Handles "700 g", "700g", "1,5 kg", "1.5kg" and multipacks such as "4 x 125 g".
    /// </summary>
    public static int ExtractGrams(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FallbackGrams;
        }

        var tokens = Tokenize(name);

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryReadQuantity(tokens, i, out var grams, out var consumed))
            {
                continue;
            }

            // Multipack: a bare count immediately before an "x" applies to the weight that follows,
            // e.g. "4 x 125 g" is 500 g, not 125 g.
            var multiplier = ReadMultiplier(tokens, i);
            var total = grams * multiplier;

            if (total is > 0 and <= MaxReasonableGrams)
            {
                return (int)Math.Round(total, MidpointRounding.AwayFromZero);
            }

            i += consumed - 1;
        }

        return FallbackGrams;
    }

    private static string[] Tokenize(string name)
    {
        // Keep digits, decimal separators and letters together; split on everything else.
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or ',')
            {
                current.Append(character);
            }
            else if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return [.. parts];
    }

    /// <summary>Reads a weight starting at <paramref name="index"/>, fused ("700g") or split ("700 g").</summary>
    private static bool TryReadQuantity(string[] tokens, int index, out decimal grams, out int consumed)
    {
        grams = 0;
        consumed = 1;

        var token = tokens[index];
        string numberPart = token;
        string unitPart = string.Empty;

        // "1,5kg" / "700g" — a unit fused onto the number.
        foreach (var suffix in new[] { "kg", "g" })
        {
            if (token.Length > suffix.Length && token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                numberPart = token[..^suffix.Length];
                unitPart = suffix;
                break;
            }
        }

        if (!TryParseNumber(numberPart, out var number))
        {
            return false;
        }

        // "700 g" — the unit is the next token. Only accept a token that is *just* the unit,
        // so "500 gram kylling" works but "2 gulrot" is not read as 2 grams.
        if (unitPart.Length == 0)
        {
            if (index + 1 >= tokens.Length)
            {
                return false;
            }

            var next = tokens[index + 1].ToLowerInvariant();
            unitPart = next switch
            {
                "kg" or "kilo" or "kilogram" => "kg",
                "g" or "gram" or "grams" => "g",
                _ => string.Empty
            };

            if (unitPart.Length == 0)
            {
                return false;
            }

            consumed = 2;
        }

        grams = unitPart == "kg" ? number * 1000 : number;
        return grams > 0;
    }

    /// <summary>Returns the multipack count preceding a weight, e.g. the 4 in "4 x 125 g".</summary>
    private static decimal ReadMultiplier(string[] tokens, int weightIndex)
    {
        if (weightIndex < 2)
        {
            return 1;
        }

        var separator = tokens[weightIndex - 1].ToLowerInvariant();
        if (separator is not ("x" or "×"))
        {
            return 1;
        }

        return TryParseNumber(tokens[weightIndex - 2], out var count) && count is > 0 and <= 100
            ? count
            : 1;
    }

    private static bool TryParseNumber(string value, out decimal number)
    {
        // Norwegian product names use a decimal comma; the invariant parser wants a dot.
        var normalized = value.Replace(',', '.');

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out number);
    }
}
