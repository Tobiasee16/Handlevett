namespace Handlevett.Models;

public sealed record MealRecipe(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlySet<string> Tags,
    int PrepMinutes,
    int Servings,
    IReadOnlyList<string>? Steps = null)
{
    public IReadOnlyList<string> RecipeSteps => Steps ?? [];
}

public sealed record RecipeIngredient(
    string IngredientKey,
    string DisplayName,
    int Grams);
