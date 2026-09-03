namespace Handlevett.Models;

public sealed record GroceryProduct(
    string Id,
    string IngredientKey,
    string Name,
    string Store,
    decimal PackagePrice,
    int PackageGrams,
    int DiscountPercent,
    decimal CaloriesPer100Grams,
    decimal ProteinPer100Grams,
    string Category)
{
    public decimal PricePerGram => PackageGrams <= 0 ? 0 : PackagePrice / PackageGrams;

    public decimal PricePer100Grams => PricePerGram * 100;

    /// <summary>Comparative unit price. This is what the UI shows — Norwegian shoppers compare per kilo.</summary>
    public decimal PricePerKilo => PricePerGram * 1000;
}
