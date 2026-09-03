using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Handlevett.Models;
using Handlevett.Services;

namespace Handlevett.Pages;

public class IndexModel(
    IMealRecommendationService recommendationService,
    IGroceryPriceProvider groceryPriceProvider,
    AppRuntimeStatus runtimeStatus) : PageModel
{
    [BindProperty]
    public PlannerRequest Planner { get; set; } = new();

    public IReadOnlyList<MealRecommendation> Recommendations { get; private set; } = [];

    public IReadOnlyList<string> Stores { get; private set; } = [];

    public IReadOnlyList<BestBuyItem> BestBuys { get; private set; } = [];

    public int AvailableProductCount { get; private set; }

    public bool HasProducts => AvailableProductCount > 0;

    public IReadOnlyList<string> PinnedIngredients => Planner.PinnedIngredients;

    public decimal EstimatedPlanCost => Recommendations.Take(3).Sum(r => r.TotalCost);

    public int EstimatedPlanCalories => Recommendations.Take(3).Sum(r => r.TotalCalories);

    public string PriceSourceText => runtimeStatus.GroceryPrices.Summary;

    public string PriceSourceDetail => runtimeStatus.GroceryPrices.Detail;

    public string RecipeSourceText => runtimeStatus.Recipes.Summary;

    public string RecipeSourceDetail => runtimeStatus.Recipes.Detail;

    /// <summary>True when prices came from Kassal.app rather than a fallback.</summary>
    public bool HasLivePrices => runtimeStatus.GroceryPrices.ItemCount > 0;

    /// <summary>True when at least one recipe came from Ollama rather than the static set.</summary>
    public bool HasAiRecipes => runtimeStatus.Recipes.ItemCount > 0;

    public async Task OnGetAsync([FromQuery] string? pinned, CancellationToken cancellationToken)
    {
        await LoadPageDataAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(pinned))
        {
            Planner.PinnedIngredients =
            [
                .. pinned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
        }
        Recommendations = await recommendationService.RecommendAsync(Planner, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadPageDataAsync(cancellationToken);

        // Even when the submitted values are out of range, recommend against what the
        // user actually typed rather than silently resetting to defaults — the form
        // re-renders with their input and the validation message beside it.
        Recommendations = await recommendationService.RecommendAsync(Planner, cancellationToken);
        return Page();
    }

    /// <summary>Norwegian labels for the internal (English) recipe tags.</summary>
    public static string TagLabel(string tag) => tag switch
    {
        "high-protein" => "Mye protein",
        "vegetarian" => "Vegetar",
        "meal-prep" => "Matprep",
        "quick" => "Rask",
        "cheap" => "Billig",
        _ => tag.Replace("-", " ")
    };

    private async Task LoadPageDataAsync(CancellationToken cancellationToken)
    {
        var products = await groceryPriceProvider.GetCurrentProductsAsync(cancellationToken);
        AvailableProductCount = products.Count;

        Stores = products
            .Select(p => p.Store)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .Prepend("Any")
            .ToArray();

        BestBuys = ComputeBestBuys(products);
    }

    private static IReadOnlyList<BestBuyItem> ComputeBestBuys(IReadOnlyList<GroceryProduct> products)
    {
        if (products.Count == 0)
        {
            return [];
        }

        var items = new List<BestBuyItem>();

        var topDiscount = products
            .Where(p => p.DiscountPercent > 0)
            .OrderByDescending(p => p.DiscountPercent)
            .FirstOrDefault();
        if (topDiscount is not null)
        {
            items.Add(new BestBuyItem(
                "discount",
                "Dagens tilbud",
                Truncate(topDiscount.Name, 30),
                topDiscount.Store,
                $"−{topDiscount.DiscountPercent} %",
                topDiscount.DiscountPercent));
        }

        var bestProteinValue = products
            .Where(p => p.ProteinPer100Grams > 0 && p.PricePer100Grams > 0)
            .OrderByDescending(p => p.ProteinPer100Grams / p.PricePer100Grams)
            .FirstOrDefault();
        if (bestProteinValue is not null)
        {
            var gramsPerKrone = bestProteinValue.ProteinPer100Grams / bestProteinValue.PricePer100Grams;
            items.Add(new BestBuyItem(
                "protein",
                "Mest protein per krone",
                Truncate(bestProteinValue.Name, 30),
                bestProteinValue.Store,
                $"{Format.Number(gramsPerKrone, 1)} g/kr",
                bestProteinValue.DiscountPercent));
        }

        var cheapestCalories = products
            .Where(p => p.CaloriesPer100Grams > 80 && p.PricePer100Grams > 0)
            .OrderBy(p => p.PricePer100Grams / p.CaloriesPer100Grams)
            .FirstOrDefault();
        if (cheapestCalories is not null)
        {
            items.Add(new BestBuyItem(
                "calories",
                "Billigste energi",
                Truncate(cheapestCalories.Name, 30),
                cheapestCalories.Store,
                Format.KrPerKg(cheapestCalories.PricePerKilo),
                cheapestCalories.DiscountPercent));
        }

        var cheapestVeg = products
            .Where(p => p.Category == "vegetable")
            .OrderBy(p => p.PricePer100Grams)
            .FirstOrDefault();
        if (cheapestVeg is not null)
        {
            items.Add(new BestBuyItem(
                "vegetable",
                "Billigste grønnsak",
                Truncate(cheapestVeg.Name, 30),
                cheapestVeg.Store,
                Format.KrPerKg(cheapestVeg.PricePerKilo),
                cheapestVeg.DiscountPercent));
        }

        return items;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    public sealed record BestBuyItem(
        string Kind,
        string Label,
        string ProductName,
        string Store,
        string Metric,
        int DiscountPercent);
}
