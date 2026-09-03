using Microsoft.AspNetCore.Mvc.RazorPages;
using Handlevett.Models;
using Handlevett.Services;

namespace Handlevett.Pages;

public class ProductsModel(IGroceryPriceProvider groceryPriceProvider) : PageModel
{
    private static readonly HashSet<string> MeatKeys =
        new(["chicken", "beef", "pork", "lamb", "turkey"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FishKeys =
        new(["salmon", "cod", "tuna", "shrimp", "trout"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<GroceryProduct> Products { get; private set; } = [];

    public IReadOnlyList<string> Stores { get; private set; } = [];

    public IReadOnlyList<string> Categories { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var all = await groceryPriceProvider.GetCurrentProductsAsync(cancellationToken);
        Products = [.. all.OrderBy(p => p.PricePerKilo)];
        Stores = [.. all.Select(p => p.Store).Distinct(StringComparer.OrdinalIgnoreCase).Order()];
        Categories = [.. all.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order()];
    }

    public static string ProteinSubtype(GroceryProduct product)
    {
        if (product.Category != "protein") return "";
        if (MeatKeys.Contains(product.IngredientKey)) return "meat";
        if (FishKeys.Contains(product.IngredientKey)) return "fish";
        return "plant";
    }

    /// <summary>Norwegian labels for the internal (English) product categories.</summary>
    public static string CategoryLabel(string category) => category switch
    {
        "protein" => "Protein",
        "vegetable" => "Grønnsak",
        "staple" => "Basisvare",
        "dairy" => "Meieri",
        "sauce" => "Saus",
        _ => category
    };
}
