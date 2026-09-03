namespace Handlevett.Services;

/// <summary>
/// Resolves a raw ingredient string — a key from the recipe model, or a store's product name —
/// to a canonical ingredient key.
/// <para>
/// The previous implementation scanned an alias dictionary with <c>Contains</c> in both
/// directions and returned the first hit, so the winner depended on dictionary iteration order
/// and changed as the product list changed. This resolves in a fixed order of decreasing
/// confidence and, among candidates of equal kind, prefers the longest alias — the most specific
/// match — breaking any remaining tie alphabetically so the result is always reproducible.
/// </para>
/// </summary>
public sealed class IngredientKeyResolver
{
    private readonly Dictionary<string, string> aliases;
    private readonly HashSet<string> canonicalKeys;
    private readonly List<KeyValuePair<string, string>> byLengthDescending;

    /// <param name="aliasToKey">Alias (raw, un-normalised) to canonical ingredient key.</param>
    public IngredientKeyResolver(IEnumerable<KeyValuePair<string, string>> aliasToKey)
    {
        aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        canonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (alias, key) in aliasToKey)
        {
            canonicalKeys.Add(key);

            var normalized = Normalize(alias);
            if (normalized.Length > 0)
            {
                aliases.TryAdd(normalized, key);
            }

            // The key itself is always an alias for itself.
            var normalizedKey = Normalize(key);
            if (normalizedKey.Length > 0)
            {
                aliases.TryAdd(normalizedKey, key);
            }
        }

        byLengthDescending = [.. aliases
            .OrderByDescending(pair => pair.Key.Length)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)];
    }

    /// <summary>Lowercase letters and digits only — Norwegian letters are kept, so "smør" does
    /// not collapse into "smr", so "Peanøttsmør 350 g" and "peanut-butter" compare cleanly.</summary>
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));

    /// <summary>Returns the canonical ingredient key, or null when nothing matches confidently.</summary>
    public string? Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // 1. The value is already a canonical key.
        if (canonicalKeys.TryGetValue(raw, out var exact))
        {
            return exact;
        }

        var normalized = Normalize(raw);
        if (normalized.Length == 0)
        {
            return null;
        }

        // 2. Exact alias match.
        if (aliases.TryGetValue(normalized, out var aliased))
        {
            return aliased;
        }

        // 3. Longest alias contained in the input — "solvingekyllingfilet700g" contains
        //    "kyllingfilet". Requires at least 4 characters so short aliases like "ris" do not
        //    latch onto unrelated words.
        foreach (var (alias, key) in byLengthDescending)
        {
            if (alias.Length >= 4 && normalized.Contains(alias, StringComparison.Ordinal))
            {
                return key;
            }
        }

        // 4. The input is a prefix or fragment of a longer alias — "kylling" for "kyllingfilet".
        //    Same length floor, same longest-first ordering.
        if (normalized.Length >= 4)
        {
            foreach (var (alias, key) in byLengthDescending)
            {
                if (alias.Contains(normalized, StringComparison.Ordinal))
                {
                    return key;
                }
            }
        }

        return null;
    }
}
