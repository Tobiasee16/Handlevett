namespace Handlevett.Models;

public sealed record MealRecommendation(
    MealRecipe Recipe,
    IReadOnlyList<PricedIngredient> Ingredients,
    decimal TotalCost,
    int TotalCalories,
    decimal TotalProtein,
    decimal Score,
    IReadOnlyList<string> Reasons)
{
    public decimal CostPerServing => Recipe.Servings <= 0 ? TotalCost : TotalCost / Recipe.Servings;

    public int CaloriesPerServing => Recipe.Servings <= 0 ? TotalCalories : TotalCalories / Recipe.Servings;

    /// <summary>
    /// Protein for one portion. Every nutrient shown on a meal card is per portion — showing one
    /// figure per portion beside another as a recipe total invites a comparison between scales.
    /// </summary>
    public decimal ProteinPerServing => Recipe.Servings <= 0
        ? TotalProtein
        : decimal.Round(TotalProtein / Recipe.Servings, 1);
}

public sealed record PricedIngredient(
    RecipeIngredient Ingredient,
    GroceryProduct Product,
    decimal EstimatedCost,
    int Calories,
    decimal Protein);
