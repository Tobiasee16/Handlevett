namespace Handlevett.Services;

/// <summary>
/// Norwegian display names for the internal (English) ingredient keys.
/// <para>
/// The database is the real source of these — <c>Ingredient.NameNo</c> — and the read path uses
/// it. This is the in-memory fallback used while validating freshly generated recipes, before
/// they have been reconciled against the catalogue, and for any key the catalogue does not know.
/// </para>
/// </summary>
public static class IngredientDisplayNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chicken"] = "Kyllingfilet",
        ["beef"] = "Karbonadedeig",
        ["pork"] = "Svinefilet",
        ["salmon"] = "Laks",
        ["cod"] = "Torsk",
        ["tofu"] = "Tofu",
        ["eggs"] = "Egg",
        ["beans"] = "Bønner",
        ["rice"] = "Ris",
        ["pasta"] = "Pasta",
        ["potato"] = "Potet",
        ["oats"] = "Havregryn",
        ["broccoli"] = "Brokkoli",
        ["cabbage"] = "Hodekål",
        ["carrot"] = "Gulrot",
        ["spinach"] = "Spinat",
        ["tomato"] = "Tomat",
        ["yoghurt"] = "Gresk yoghurt",
        ["cottage-cheese"] = "Cottage cheese",
        ["peanut-butter"] = "Peanøttsmør",
        ["salsa"] = "Salsa"
    };

    /// <summary>Falls back to a title-cased version of the key for anything unknown.</summary>
    public static string For(string ingredientKey)
    {
        if (Names.TryGetValue(ingredientKey, out var name))
        {
            return name;
        }

        return string.Join(' ', ingredientKey
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
