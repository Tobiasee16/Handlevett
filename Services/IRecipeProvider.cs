using Handlevett.Models;

namespace Handlevett.Services;

/// <summary>Curated recipes, read from the database after being seeded at startup.</summary>
public interface IRecipeProvider
{
    Task<IReadOnlyList<MealRecipe>> GetRecipesAsync(CancellationToken cancellationToken = default);
}
