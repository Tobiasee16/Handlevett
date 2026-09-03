using Handlevett.Models;

namespace Handlevett.Services;

public interface IRecipeGenerator
{
    Task<IReadOnlyList<MealRecipe>> GenerateRecipesAsync(
        PlannerRequest request,
        IReadOnlyList<GroceryProduct> products,
        CancellationToken cancellationToken = default);
}
