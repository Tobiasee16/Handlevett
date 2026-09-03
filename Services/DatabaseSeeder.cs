using Handlevett.Data;
using Microsoft.EntityFrameworkCore;

namespace Handlevett.Services;

/// <summary>
/// Applies migrations and seeds reference data.
/// <para>
/// The ingredient catalogue used to be a <c>static readonly</c> array inside the Kassal.app HTTP
/// client, so adding an ingredient meant a recompile. It now lives in the database; this class
/// puts the initial set there and is idempotent, so it is safe to run on every start.
/// </para>
/// </summary>
public sealed class DatabaseSeeder(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<DatabaseSeeder> logger)
{
    private sealed record IngredientSeed(
        string Key,
        string NameNo,
        string SearchTerm,
        string Category,
        string[] Aliases);

    private sealed record RecipeSeed(
        string Slug,
        string Name,
        string Description,
        int PrepMinutes,
        int Servings,
        string[] Tags,
        (string Key, int Grams)[] Ingredients,
        string[] Steps);

    // Category defaults, used when a product carries no nutrition data of its own.
    private static readonly Dictionary<string, (decimal Kcal, decimal Protein)> CategoryDefaults = new()
    {
        ["protein"] = (140, 18),
        ["staple"] = (340, 8),
        ["vegetable"] = (35, 2),
        ["dairy"] = (95, 10),
        ["sauce"] = (100, 3)
    };

    private static readonly IngredientSeed[] Ingredients =
    [
        new("chicken",        "Kyllingfilet",   "kyllingfilet",              "protein",   ["kylling", "kyllingbryst"]),
        new("beef",           "Karbonadedeig",  "storfekjøtt karbonadedeig", "protein",   ["storfekjøtt", "kjøttdeig", "karbonade"]),
        new("pork",           "Svinefilet",     "svinefilet",                "protein",   ["svin", "svinekjøtt"]),
        new("salmon",         "Laks",           "laks filet",                "protein",   ["laksefilet"]),
        new("cod",            "Torsk",          "torsk filet",               "protein",   ["torskefilet"]),
        new("tofu",           "Tofu",           "tofu",                      "protein",   []),
        new("eggs",           "Egg",            "egg",                       "protein",   ["egga"]),
        new("beans",          "Bønner",         "bønner",                    "protein",   ["sortebønner", "kidneybønner"]),
        new("rice",           "Ris",            "ris",                       "staple",    ["jasminris", "basmatiris", "langkornet ris"]),
        new("pasta",          "Pasta",          "pasta",                     "staple",    ["spaghetti", "penne", "fusilli"]),
        new("potato",         "Potet",          "potet",                     "staple",    ["poteter", "mandelpotet"]),
        new("oats",           "Havregryn",      "havregryn",                 "staple",    ["havre", "lettkokte havregryn"]),
        new("broccoli",       "Brokkoli",       "brokkoli",                  "vegetable", []),
        new("cabbage",        "Hodekål",        "hodekål",                   "vegetable", ["kål", "spisskål"]),
        new("carrot",         "Gulrot",         "gulrot",                    "vegetable", ["gulrøtter"]),
        new("spinach",        "Spinat",         "spinat",                    "vegetable", ["babyspinat"]),
        new("tomato",         "Tomat",          "tomat",                     "vegetable", ["tomater", "hermetiske tomater"]),
        new("yoghurt",        "Gresk yoghurt",  "gresk yoghurt",             "dairy",     ["yoghurt"]),
        new("cottage-cheese", "Cottage cheese", "cottage cheese",            "dairy",     ["kesam"]),
        new("peanut-butter",  "Peanøttsmør",    "peanøttsmør",               "staple",    ["peanottsmor"]),
        new("salsa",          "Salsa",          "salsa",                     "sauce",     ["tacosaus"])
    ];

    private static readonly RecipeSeed[] CuratedRecipes =
    [
        new("chicken-rice-bowl",
            "Kyllingbolle med ris",
            "Mager kylling, ris, brokkoli, gulrot og salsa – en billig og proteinrik basismiddag.",
            35, 3,
            ["high-protein", "meal-prep"],
            [("chicken", 450), ("rice", 260), ("broccoli", 300), ("carrot", 250), ("salsa", 120)],
            [
                "Kok risen etter anvisningen på pakken.",
                "Stek kyllingen i biter til den er gjennomstekt.",
                "Damp brokkoli og gulrot til de er akkurat møre.",
                "Fordel i skåler og topp med salsa."
            ]),

        new("tofu-cabbage-stir-fry",
            "Wok med tofu og kål",
            "Sprø kål og tofu over ris, satt sammen etter hvilke grønnsaker som er på tilbud.",
            30, 3,
            ["vegetarian", "meal-prep"],
            [("tofu", 400), ("cabbage", 500), ("rice", 220), ("carrot", 200), ("salsa", 90)],
            [
                "Press tofuen tørr og skjær den i terninger.",
                "Kok risen mens du strimler kål og gulrot.",
                "Stek tofuen gyllen, tilsett grønnsakene og wok raskt.",
                "Rør inn salsaen helt til slutt og server over ris."
            ]),

        new("egg-potato-skillet",
            "Eggepanne med potet",
            "Mettende panne med egg, potet, spinat og tomat.",
            25, 2,
            ["vegetarian", "quick"],
            [("eggs", 300), ("potato", 700), ("spinach", 120), ("tomato", 250)],
            [
                "Skjær potetene i terninger og stek dem sprø i panna.",
                "Tilsett tomat og spinat og la det falle sammen.",
                "Slå eggene over og stek til de har satt seg.",
                "Del i porsjoner og server rett fra panna."
            ]),

        new("pasta-bean-pot",
            "Pastagryte med bønner",
            "Rimelig pasta med bønner, tomat, gulrot og spinat – laget for storkok.",
            40, 4,
            ["vegetarian", "meal-prep"],
            [("pasta", 320), ("beans", 460), ("tomato", 350), ("carrot", 300), ("spinach", 100)],
            [
                "Fres gulrot og tomat til en enkel saus.",
                "Tilsett bønnene og la gryta småkoke.",
                "Kok pastaen al dente og bland den inn.",
                "Vend inn spinaten rett før servering."
            ]),

        new("protein-oats",
            "Proteingrøt med havre",
            "Billig havregrøt med yoghurt, peanøttsmør og cottage cheese.",
            10, 3,
            ["high-protein", "quick", "vegetarian"],
            [("oats", 240), ("yoghurt", 450), ("peanut-butter", 80), ("cottage-cheese", 250)],
            [
                "Kok havregrynene raskt opp med vann.",
                "Rør inn cottage cheese mens grøten er varm.",
                "Topp med gresk yoghurt og peanøttsmør."
            ])
    ];

    public async Task MigrateAndSeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Replaces EnsureCreated(): the schema is now versioned, so it can evolve without the
        // database file having to be deleted by hand.
        await db.Database.MigrateAsync(cancellationToken);

        await SeedIngredientsAsync(db, cancellationToken);
        await SeedCuratedRecipesAsync(db, cancellationToken);
    }

    private async Task SeedIngredientsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Ingredients
            .Include(i => i.Aliases)
            .ToDictionaryAsync(i => i.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var addedIngredients = 0;
        var addedAliases = 0;

        foreach (var seed in Ingredients)
        {
            var defaults = CategoryDefaults.GetValueOrDefault(seed.Category, (100m, 3m));

            if (!existing.TryGetValue(seed.Key, out var ingredient))
            {
                ingredient = new Ingredient
                {
                    Key = seed.Key,
                    NameNo = seed.NameNo,
                    SearchTerm = seed.SearchTerm,
                    Category = seed.Category,
                    DefaultKcalPer100g = defaults.Item1,
                    DefaultProteinPer100g = defaults.Item2,
                    IsActive = true
                };
                db.Ingredients.Add(ingredient);
                existing[seed.Key] = ingredient;
                addedIngredients++;
            }

            // Aliases are additive: a name learned from a product should never be wiped by a reseed.
            var known = ingredient.Aliases
                .Select(a => a.Alias)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var alias in seed.Aliases.Append(seed.NameNo).Append(seed.SearchTerm).Append(seed.Key))
            {
                var normalized = IngredientKeyResolver.Normalize(alias);

                if (normalized.Length == 0 || !known.Add(normalized))
                {
                    continue;
                }

                ingredient.Aliases.Add(new IngredientAlias
                {
                    Alias = normalized,
                    Source = "seed",
                    Confidence = 1.0
                });
                addedAliases++;
            }
        }

        if (addedIngredients > 0 || addedAliases > 0)
        {
            // Aliases are globally unique; a normalised form claimed by an earlier ingredient is
            // dropped rather than failing the whole seed.
            await SaveIgnoringAliasConflictsAsync(db, cancellationToken);
            logger.LogInformation(
                "Seed: {Ingredients} ingredients, {Aliases} aliases added.",
                addedIngredients, addedAliases);
        }
    }

    private async Task SeedCuratedRecipesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingSlugs = await db.Recipes
            .Where(r => r.Source == RecipeSource.Curated)
            .Select(r => r.Slug)
            .ToListAsync(cancellationToken);

        var known = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = CuratedRecipes.Where(r => !known.Contains(r.Slug)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var ingredientIds = await db.Ingredients
            .ToDictionaryAsync(i => i.Key, i => i.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var seed in missing)
        {
            if (!seed.Ingredients.All(i => ingredientIds.ContainsKey(i.Key)))
            {
                logger.LogWarning("Seed: skipping recipe {Slug} — unknown ingredient key.", seed.Slug);
                continue;
            }

            var recipe = new Recipe
            {
                Slug = seed.Slug,
                Name = seed.Name,
                Description = seed.Description,
                Servings = seed.Servings,
                PrepMinutes = seed.PrepMinutes,
                Source = RecipeSource.Curated,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var (key, grams) in seed.Ingredients)
            {
                recipe.Ingredients.Add(new RecipeIngredientRow
                {
                    IngredientId = ingredientIds[key],
                    Grams = grams
                });
            }

            for (var i = 0; i < seed.Steps.Length; i++)
            {
                recipe.Steps.Add(new RecipeStep { Ordinal = i + 1, Text = seed.Steps[i] });
            }

            foreach (var tag in seed.Tags)
            {
                recipe.Tags.Add(new RecipeTag { Tag = tag });
            }

            db.Recipes.Add(recipe);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed: {Count} curated recipes added.", missing.Length);
    }

    private async Task SaveIgnoringAliasConflictsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Seed: alias conflict, retrying without the duplicates.");

            foreach (var entry in db.ChangeTracker.Entries<IngredientAlias>().ToArray())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
