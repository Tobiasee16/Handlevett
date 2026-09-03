using Handlevett.Models;
using Handlevett.Services;
using Microsoft.Extensions.Options;

namespace Handlevett.Tests;

/// <summary>
/// Scoring is the product: it decides what the app tells you to cook. These tests pin the
/// behaviour the weights are supposed to produce, which was untestable while they were literals
/// inside a static method.
/// </summary>
public class MealRecommendationServiceTests
{
    // ── Test doubles ──

    private sealed class StubPrices(params GroceryProduct[] products) : IGroceryPriceProvider
    {
        public Task<IReadOnlyList<GroceryProduct>> GetCurrentProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GroceryProduct>>(products);
    }

    private sealed class StubRecipes(params MealRecipe[] recipes) : IRecipeProvider
    {
        public Task<IReadOnlyList<MealRecipe>> GetRecipesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MealRecipe>>(recipes);
    }

    private static GroceryProduct Product(
        string key,
        decimal packagePrice = 100,
        int grams = 1000,
        int discount = 0,
        decimal kcal = 100,
        decimal protein = 10,
        string store = "KIWI",
        string category = "protein") =>
        new($"{key}-1", key, $"{key} produkt", store, packagePrice, grams, discount, kcal, protein, category);

    private static MealRecipe Recipe(
        string id,
        (string Key, int Grams)[] ingredients,
        string[]? tags = null,
        int servings = 2) =>
        new(id, $"Rett {id}", "Beskrivelse", [.. ingredients.Select(i => new RecipeIngredient(i.Key, i.Key, i.Grams))],
            (tags ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase), 30, servings);

    private static MealRecommendationService Build(
        IGroceryPriceProvider prices,
        IRecipeProvider recipes,
        ScoringWeights? weights = null) =>
        new(prices, recipes, [], Options.Create(weights ?? new ScoringWeights()));

    // ── Tests ──

    [Fact]
    public async Task SkipsRecipesWhoseIngredientsAreNotAvailable()
    {
        // Only chicken is stocked, so a recipe needing rice cannot be costed and must not appear
        // with a fabricated price.
        var service = Build(
            new StubPrices(Product("chicken")),
            new StubRecipes(
                Recipe("has-all", [("chicken", 200)]),
                Recipe("missing-one", [("chicken", 200), ("rice", 100)])));

        var result = await service.RecommendAsync(new PlannerRequest());

        Assert.Single(result);
        Assert.Equal("has-all", result[0].Recipe.Id);
    }

    [Fact]
    public async Task PricesIngredientsByWeight()
    {
        // 100 kr per 1000 g = 0,10 kr/g; 250 g costs 25 kr.
        var service = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000)),
            new StubRecipes(Recipe("r", [("chicken", 250)])));

        var result = await service.RecommendAsync(new PlannerRequest());

        Assert.Equal(25m, result[0].TotalCost);
    }

    [Fact]
    public async Task RanksTheCheaperRecipeHigherWhenNothingElseDiffers()
    {
        var service = Build(
            new StubPrices(
                Product("chicken", packagePrice: 100, grams: 1000),
                Product("tofu", packagePrice: 20, grams: 1000)),
            new StubRecipes(
                Recipe("expensive", [("chicken", 500)]),
                Recipe("cheap", [("tofu", 500)])));

        var result = await service.RecommendAsync(new PlannerRequest { HighProtein = false });

        Assert.Equal("cheap", result[0].Recipe.Id);
    }

    [Fact]
    public async Task PenalisesGoingOverBudget()
    {
        var overBudget = Build(
            new StubPrices(Product("chicken", packagePrice: 1000, grams: 1000)),
            new StubRecipes(Recipe("r", [("chicken", 500)])));

        var withinBudget = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000)),
            new StubRecipes(Recipe("r", [("chicken", 500)])));

        var request = new PlannerRequest { BudgetNok = 200 };

        var expensive = await overBudget.RecommendAsync(request);
        var affordable = await withinBudget.RecommendAsync(request);

        Assert.True(expensive[0].Score < affordable[0].Score);
    }

    [Fact]
    public async Task WeightsAreActuallyApplied()
    {
        // Zeroing the budget weight must change the ranking — proof the configured values are
        // read rather than the old hardcoded constants.
        var prices = new StubPrices(
            Product("chicken", packagePrice: 100, grams: 1000),
            Product("tofu", packagePrice: 20, grams: 1000));

        var recipes = new StubRecipes(
            Recipe("expensive", [("chicken", 500)]),
            Recipe("cheap", [("tofu", 500)]));

        var request = new PlannerRequest { HighProtein = false, BudgetNok = 2000, TargetCalories = 5000 };

        var normal = await Build(prices, recipes).RecommendAsync(request);
        var noBudgetWeight = await Build(prices, recipes, new ScoringWeights { Budget = 0 })
            .RecommendAsync(request);

        Assert.Equal("cheap", normal[0].Recipe.Id);
        Assert.NotEqual(normal[0].Score, noBudgetWeight[0].Score);
    }

    [Fact]
    public async Task ExcludesNonVegetarianRecipesWhenVegetarianIsRequested()
    {
        var service = Build(
            new StubPrices(Product("chicken"), Product("tofu")),
            new StubRecipes(
                Recipe("meat", [("chicken", 200)]),
                Recipe("veg", [("tofu", 200)], ["vegetarian"])));

        var result = await service.RecommendAsync(new PlannerRequest { Vegetarian = true });

        Assert.Single(result);
        Assert.Equal("veg", result[0].Recipe.Id);
    }

    [Fact]
    public async Task RestrictsToPinnedIngredients()
    {
        var service = Build(
            new StubPrices(Product("chicken"), Product("tofu")),
            new StubRecipes(
                Recipe("chicken-dish", [("chicken", 200)]),
                Recipe("tofu-dish", [("tofu", 200)])));

        var result = await service.RecommendAsync(new PlannerRequest { PinnedIngredients = ["tofu"] });

        Assert.Single(result);
        Assert.Equal("tofu-dish", result[0].Recipe.Id);
    }

    [Fact]
    public async Task PrefersTheChosenStoreEvenWhenItIsNotTheCheapest()
    {
        var service = Build(
            new StubPrices(
                Product("chicken", packagePrice: 50, grams: 1000, store: "REMA 1000"),
                Product("chicken", packagePrice: 90, grams: 1000, store: "KIWI")),
            new StubRecipes(Recipe("r", [("chicken", 1000)])));

        var result = await service.RecommendAsync(new PlannerRequest { PreferredStore = "KIWI" });

        Assert.Equal("KIWI", result[0].Ingredients[0].Product.Store);
        Assert.Equal(90m, result[0].TotalCost);
    }

    [Fact]
    public async Task ReportsPerServingFiguresFromTheRecipeYield()
    {
        var service = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000, kcal: 200)),
            new StubRecipes(Recipe("r", [("chicken", 400)], servings: 4)));

        var result = await service.RecommendAsync(new PlannerRequest());

        Assert.Equal(40m, result[0].TotalCost);
        Assert.Equal(10m, result[0].CostPerServing);
        Assert.Equal(800, result[0].TotalCalories);
        Assert.Equal(200, result[0].CaloriesPerServing);
    }

    [Fact]
    public async Task ReportsProteinPerServingNotTheRecipeTotal()
    {
        // 20 g protein per 100 g over 400 g = 80 g for the recipe, 20 g per portion.
        // The card shows calories per portion, so protein beside it must use the same scale.
        var service = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000, protein: 20)),
            new StubRecipes(Recipe("r", [("chicken", 400)], servings: 4)));

        var result = await service.RecommendAsync(new PlannerRequest());

        Assert.Equal(80m, result[0].TotalProtein);
        Assert.Equal(20m, result[0].ProteinPerServing);
    }

    [Fact]
    public async Task FallsBackToTheTotalWhenARecipeHasNoServings()
    {
        var service = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000, protein: 20)),
            new StubRecipes(Recipe("r", [("chicken", 400)], servings: 0)));

        var result = await service.RecommendAsync(new PlannerRequest());

        Assert.Equal(result[0].TotalProtein, result[0].ProteinPerServing);
    }

    [Fact]
    public async Task ExplainsItsRanking()
    {
        var service = Build(
            new StubPrices(Product("chicken", packagePrice: 100, grams: 1000, discount: 30)),
            new StubRecipes(Recipe("r", [("chicken", 200)])));

        var result = await service.RecommendAsync(new PlannerRequest { BudgetNok = 500 });

        Assert.NotEmpty(result[0].Reasons);
        Assert.Contains(result[0].Reasons, reason => reason.Contains("30 %"));
    }

    [Fact]
    public async Task ReturnsNothingWhenThereAreNoProducts()
    {
        var service = Build(new StubPrices(), new StubRecipes(Recipe("r", [("chicken", 200)])));

        Assert.Empty(await service.RecommendAsync(new PlannerRequest()));
    }
}
