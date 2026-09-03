using Handlevett.Data;
using Handlevett.Models;
using Microsoft.EntityFrameworkCore;

namespace Handlevett.Services;

/// <summary>
/// Reads recipes from the database and maps them to the domain model.
/// <para>
/// Generated recipes used to be stored as a serialised JSON blob in a cache table, which meant
/// they could only be used whole and never queried. They are now ordinary rows, so a generated
/// recipe can be filtered, deduplicated and counted like any other.
/// </para>
/// </summary>
public sealed class DatabaseRecipeProvider(
    IDbContextFactory<AppDbContext> dbContextFactory) : IRecipeProvider
{
    public async Task<IReadOnlyList<MealRecipe>> GetRecipesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadAsync(db, r => r.Source == RecipeSource.Curated, cancellationToken);
    }

    /// <summary>
    /// Generated recipes matching a generation key that are still within the freshness window.
    /// Replaces the old in-memory-plus-blob cache with a plain indexed query.
    /// </summary>
    public async Task<IReadOnlyList<MealRecipe>> GetGeneratedAsync(
        string generationKey,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - maxAge;

        return await LoadAsync(
            db,
            r => r.Source == RecipeSource.Generated
                 && r.GenerationKey == generationKey
                 && r.CreatedAtUtc >= cutoff,
            cancellationToken);
    }

    /// <summary>Persists newly generated recipes. Slugs are made unique per generation key.</summary>
    public async Task SaveGeneratedAsync(
        IReadOnlyList<MealRecipe> recipes,
        string generationKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (recipes.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredientIds = await db.Ingredients
            .ToDictionaryAsync(i => i.Key, i => i.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = DateTime.UtcNow;
        var keyFragment = generationKey.Length <= 12 ? generationKey : generationKey[..12];

        foreach (var recipe in recipes)
        {
            // A generated recipe only counts if every ingredient is one we actually stock.
            if (!recipe.Ingredients.All(i => ingredientIds.ContainsKey(i.IngredientKey)))
            {
                continue;
            }

            var slug = $"{recipe.Id}-{keyFragment}";

            if (await db.Recipes.AnyAsync(r => r.Slug == slug, cancellationToken))
            {
                continue;
            }

            var row = new Recipe
            {
                Slug = slug,
                Name = recipe.Name,
                Description = recipe.Description,
                Servings = recipe.Servings,
                PrepMinutes = recipe.PrepMinutes,
                Source = RecipeSource.Generated,
                GeneratedByModel = model,
                GenerationKey = generationKey,
                CreatedAtUtc = now
            };

            foreach (var ingredient in recipe.Ingredients)
            {
                row.Ingredients.Add(new RecipeIngredientRow
                {
                    IngredientId = ingredientIds[ingredient.IngredientKey],
                    Grams = ingredient.Grams
                });
            }

            var steps = recipe.RecipeSteps;
            for (var i = 0; i < steps.Count; i++)
            {
                row.Steps.Add(new RecipeStep { Ordinal = i + 1, Text = steps[i] });
            }

            foreach (var tag in recipe.Tags)
            {
                row.Tags.Add(new RecipeTag { Tag = tag });
            }

            db.Recipes.Add(row);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes generated recipes past their useful life, so the table does not grow without bound.</summary>
    public async Task PruneGeneratedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - olderThan;

        await db.Recipes
            .Where(r => r.Source == RecipeSource.Generated && r.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<MealRecipe>> LoadAsync(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<Recipe, bool>> filter,
        CancellationToken cancellationToken)
    {
        var rows = await db.Recipes
            .AsNoTracking()
            .Where(filter)
            .Select(r => new
            {
                r.Slug,
                r.Name,
                r.Description,
                r.Servings,
                r.PrepMinutes,
                r.Source,
                Ingredients = r.Ingredients
                    .Select(i => new { i.Ingredient!.Key, i.Ingredient!.NameNo, i.Grams })
                    .ToList(),
                Steps = r.Steps.OrderBy(s => s.Ordinal).Select(s => s.Text).ToList(),
                Tags = r.Tags.Select(t => t.Tag).ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new MealRecipe(
                // The "ollama-" prefix is what the UI keys its AI badge off, so it is preserved.
                r.Source == RecipeSource.Generated ? $"ollama-{r.Slug}" : r.Slug,
                r.Name,
                r.Description,
                [.. r.Ingredients.Select(i => new RecipeIngredient(i.Key, i.NameNo, i.Grams))],
                r.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase),
                r.PrepMinutes,
                r.Servings,
                r.Steps))
        ];
    }
}
