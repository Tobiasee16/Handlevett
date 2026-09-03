namespace Handlevett.Services;

/// <summary>What to search the upstream feed for. Comes from the Ingredients table.</summary>
public sealed record FeedQuery(string IngredientKey, string SearchTerm, string Category);

public sealed record FeedNutrient(string Code, decimal Amount, string? Unit);

/// <summary>
/// A product as the upstream feed reports it, before it is reconciled against the catalogue.
/// Deliberately separate from the <c>GroceryProduct</c> domain record: this is wire data that
/// may be incomplete, and only the ingest job ever sees it.
/// </summary>
public sealed record FeedProduct(
    string ExternalId,
    string Name,
    string? Brand,
    string StoreName,
    string IngredientKey,
    decimal Price,
    int PackageGrams,
    int DiscountPercent,
    decimal? ReferencePrice,
    decimal? CaloriesPer100Grams,
    decimal? ProteinPer100Grams,
    string? ImageUrl,
    IReadOnlyList<FeedNutrient> Nutrition);

/// <summary>
/// An upstream source of grocery prices. Only the background ingest job depends on this — the
/// request path reads the database instead.
/// </summary>
public interface IProductFeed
{
    Task<IReadOnlyList<FeedProduct>> FetchAsync(
        IReadOnlyList<FeedQuery> queries,
        CancellationToken cancellationToken);
}
