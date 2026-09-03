using Handlevett.Models;
using Microsoft.Extensions.Options;

namespace Handlevett.Services;

public sealed class MealRecommendationService(
    IGroceryPriceProvider groceryPriceProvider,
    IRecipeProvider recipeProvider,
    IEnumerable<IRecipeGenerator> recipeGenerators,
    IOptions<ScoringWeights> scoringWeights) : IMealRecommendationService
{
    public async Task<IReadOnlyList<MealRecommendation>> RecommendAsync(
        PlannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var products = await groceryPriceProvider.GetCurrentProductsAsync(cancellationToken);

        // When the user has pinned specific ingredients, restrict generation to only those so the
        // model builds a recipe from exactly what was selected.
        var productsForGeneration = request.PinnedIngredients.Count > 0
            ? products
                .Where(p => request.PinnedIngredients.Contains(p.IngredientKey, StringComparer.OrdinalIgnoreCase))
                .ToArray()
            : products;

        var productsByIngredient = productsForGeneration
            .GroupBy(product => product.IngredientKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(product => IsPreferredStore(product, request.PreferredStore))
                    .ThenBy(product => product.PricePerGram)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var curated = await recipeProvider.GetRecipesAsync(cancellationToken);

        var generated = new List<MealRecipe>();
        foreach (var generator in recipeGenerators)
        {
            generated.AddRange(
                await generator.GenerateRecipesAsync(request, productsForGeneration, cancellationToken));
        }

        var weights = scoringWeights.Value;

        return
        [
            .. curated
                .Concat(generated)
                .Where(recipe => IsRecipeAllowed(recipe, request))
                .Select(recipe => PriceRecipe(recipe, request, productsByIngredient, weights))
                .OfType<MealRecommendation>()
                .OrderByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.TotalCost)
        ];
    }

    private static bool IsRecipeAllowed(MealRecipe recipe, PlannerRequest request) =>
        !request.Vegetarian || recipe.Tags.Contains("vegetarian");

    private static MealRecommendation? PriceRecipe(
        MealRecipe recipe,
        PlannerRequest request,
        IReadOnlyDictionary<string, GroceryProduct[]> productsByIngredient,
        ScoringWeights weights)
    {
        var pricedIngredients = new List<PricedIngredient>();

        foreach (var ingredient in recipe.Ingredients)
        {
            // A recipe is only offered when every ingredient can actually be bought right now.
            if (!productsByIngredient.TryGetValue(ingredient.IngredientKey, out var products))
            {
                return null;
            }

            var product = products[0];
            var cost = decimal.Round(product.PricePerGram * ingredient.Grams, 2);
            var calories = (int)Math.Round(
                product.CaloriesPer100Grams * ingredient.Grams / 100,
                MidpointRounding.AwayFromZero);
            var protein = decimal.Round(product.ProteinPer100Grams * ingredient.Grams / 100, 1);

            pricedIngredients.Add(new PricedIngredient(ingredient, product, cost, calories, protein));
        }

        var totalCost = pricedIngredients.Sum(item => item.EstimatedCost);
        var totalCalories = pricedIngredients.Sum(item => item.Calories);
        var totalProtein = pricedIngredients.Sum(item => item.Protein);
        var discountValue = pricedIngredients.Sum(item => item.Product.DiscountPercent)
                            / Math.Max(1m, pricedIngredients.Count);

        var score = Score(request, weights, totalCost, totalCalories, totalProtein, discountValue, recipe);

        return new MealRecommendation(
            recipe,
            pricedIngredients,
            totalCost,
            totalCalories,
            totalProtein,
            decimal.Round(score, 1),
            BuildReasons(recipe, request, pricedIngredients, totalCost, totalCalories, discountValue));
    }

    /// <summary>
    /// The recommendation score. Each term is a 0–1 fit multiplied by its configured weight, so a
    /// weight can be read as "how many points is this worth at best". The weights live in
    /// <see cref="ScoringWeights"/> rather than as literals here, which makes them tunable
    /// without a rebuild and testable in isolation.
    /// </summary>
    private static decimal Score(
        PlannerRequest request,
        ScoringWeights weights,
        decimal totalCost,
        int totalCalories,
        decimal totalProtein,
        decimal discountValue,
        MealRecipe recipe)
    {
        var budget = Math.Max(1m, request.BudgetNok);

        var budgetFit = Math.Max(0, 1 - (totalCost / budget));

        var calorieFit = 1 - Math.Min(
            1,
            Math.Abs(totalCalories - request.TargetCalories) / (decimal)Math.Max(1, request.TargetCalories));

        var proteinFit = request.HighProtein
            ? Math.Min(totalProtein / Math.Max(1m, weights.ProteinTargetGrams), 1)
            : weights.NeutralProteinFactor;

        var overBudget = totalCost > request.BudgetNok
            ? (totalCost - request.BudgetNok) / budget
            : 0;

        return (budgetFit * weights.Budget)
             + (calorieFit * weights.Calories)
             + (discountValue * weights.Discount)
             + (proteinFit * weights.Protein)
             + PreferenceBonus(recipe, request, weights)
             - (overBudget * weights.OverBudgetPenalty);
    }

    private static decimal PreferenceBonus(MealRecipe recipe, PlannerRequest request, ScoringWeights weights)
    {
        var bonus = 0m;

        if (request.MealPrep && recipe.Tags.Contains("meal-prep"))
        {
            bonus += weights.MealPrepBonus;
        }

        if (request.QuickMeals && recipe.Tags.Contains("quick"))
        {
            bonus += weights.QuickMealBonus;
        }

        if (request.HighProtein && recipe.Tags.Contains("high-protein"))
        {
            bonus += weights.HighProteinBonus;
        }

        return bonus;
    }

    private static IReadOnlyList<string> BuildReasons(
        MealRecipe recipe,
        PlannerRequest request,
        IReadOnlyList<PricedIngredient> ingredients,
        decimal totalCost,
        int totalCalories,
        decimal discountValue)
    {
        var reasons = new List<string>();
        var bestDiscount = ingredients.OrderByDescending(item => item.Product.DiscountPercent).First();

        if (bestDiscount.Product.DiscountPercent > 0)
        {
            reasons.Add($"{bestDiscount.Product.Name} er {bestDiscount.Product.DiscountPercent} % ned hos {bestDiscount.Product.Store}.");
        }

        reasons.Add(totalCost <= request.BudgetNok
            ? $"Holder seg innenfor budsjettet på {Format.Kr(request.BudgetNok)} med {Format.Kr(request.BudgetNok - totalCost)} til overs."
            : $"Ligger {Format.Kr(totalCost - request.BudgetNok)} over budsjett, men scorer godt på næring og tilbud.");

        reasons.Add($"Lander på {Format.Kcal(totalCalories)} fordelt på {recipe.Servings} porsjoner.");

        if (discountValue >= 20)
        {
            reasons.Add("Bruker flere råvarer som er på tilbud nå.");
        }

        if (request.MealPrep && recipe.Tags.Contains("meal-prep"))
        {
            reasons.Add("Egner seg godt til matprep og storkok.");
        }

        return reasons;
    }

    private static bool IsPreferredStore(GroceryProduct product, string preferredStore) =>
        !string.IsNullOrWhiteSpace(preferredStore)
        && !preferredStore.Equals("Any", StringComparison.OrdinalIgnoreCase)
        && product.Store.Equals(preferredStore, StringComparison.OrdinalIgnoreCase);
}
