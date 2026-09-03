using Handlevett.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Handlevett.Services;

public sealed record IngestResult(int ProductsSeen, int ProductsAdded, int PricesChanged);

/// <summary>
/// Pulls products from the upstream feed and reconciles them into the catalogue.
/// <para>
/// This is the write path, and the only thing that talks to Kassal.app. It upserts stores and
/// products, and appends to <see cref="ProductPrice"/> only when a price actually differs from
/// the last observation — where the old code wrote one identical row per product per day into a
/// table nothing ever read.
/// </para>
/// </summary>
public sealed class GroceryIngestService(
    IProductFeed feed,
    IDbContextFactory<AppDbContext> dbContextFactory,
    IOptions<IngestOptions> options,
    AppRuntimeStatus runtimeStatus,
    ILogger<GroceryIngestService> logger)
{
    /// <summary>
    /// True when the newest price observation is older than the configured freshness window, so a
    /// restart under <c>dotnet watch</c> does not re-scrape on every file save.
    /// </summary>
    public async Task<bool> IsCatalogueStaleAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var newest = await db.Products
            .AsNoTracking()
            .OrderByDescending(p => p.CurrentPriceObservedUtc)
            .Select(p => (DateTime?)p.CurrentPriceObservedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is null)
        {
            return true;
        }

        var window = TimeSpan.FromMinutes(Math.Max(1, options.Value.SkipIfFresherThanMinutes));
        return DateTime.UtcNow - newest.Value > window;
    }

    public async Task<IngestResult> RunAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var run = new IngestRun
        {
            StartedAtUtc = DateTime.UtcNow,
            Status = IngestStatus.Running
        };
        db.IngestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var ingredients = await db.Ingredients
                .Where(i => i.IsActive)
                .OrderBy(i => i.Key)
                .ToListAsync(cancellationToken);

            if (ingredients.Count == 0)
            {
                throw new InvalidOperationException("Ingen aktive råvarer å hente priser for.");
            }

            var queries = ingredients
                .Select(i => new FeedQuery(i.Key, i.SearchTerm, i.Category))
                .ToArray();

            logger.LogInformation("Ingest: fetching {Count} ingredient searches.", queries.Length);
            var feedProducts = await feed.FetchAsync(queries, cancellationToken);
            logger.LogInformation("Ingest: feed returned {Count} products.", feedProducts.Count);

            var result = await ReconcileAsync(db, ingredients, feedProducts, cancellationToken);

            run.Status = IngestStatus.Succeeded;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.ProductsSeen = result.ProductsSeen;
            run.ProductsAdded = result.ProductsAdded;
            run.PricesChanged = result.PricesChanged;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Ingest: {Seen} seen, {Added} new, {Changed} price changes.",
                result.ProductsSeen, result.ProductsAdded, result.PricesChanged);

            return result;
        }
        catch (Exception ex)
        {
            run.Status = IngestStatus.Failed;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                logger.LogWarning(saveEx, "Ingest: could not record the failed run.");
            }

            runtimeStatus.SetGroceryPrices("Innhenting feilet", ex.Message, 0);
            throw;
        }
    }

    private async Task<IngestResult> ReconcileAsync(
        AppDbContext db,
        List<Ingredient> ingredients,
        IReadOnlyList<FeedProduct> feedProducts,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var ingredientsByKey = ingredients.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);

        var stores = await db.Stores.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var externalIds = feedProducts.Select(p => p.ExternalId).ToHashSet(StringComparer.Ordinal);
        var existing = await db.Products
            .Where(p => externalIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, StringComparer.Ordinal, cancellationToken);

        var added = 0;
        var priceChanges = 0;

        foreach (var feedProduct in feedProducts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stores.TryGetValue(feedProduct.StoreName, out var store))
            {
                store = new Store { Name = feedProduct.StoreName };
                db.Stores.Add(store);
                stores[feedProduct.StoreName] = store;
            }

            ingredientsByKey.TryGetValue(feedProduct.IngredientKey, out var ingredient);

            if (!existing.TryGetValue(feedProduct.ExternalId, out var product))
            {
                product = new Product
                {
                    ExternalId = feedProduct.ExternalId,
                    Name = feedProduct.Name,
                    FirstSeenUtc = now
                };
                db.Products.Add(product);
                existing[feedProduct.ExternalId] = product;
                added++;
            }

            product.Name = feedProduct.Name;
            product.Brand = feedProduct.Brand;
            product.Store = store;
            product.Ingredient = ingredient;
            product.PackageGrams = feedProduct.PackageGrams;
            product.ImageUrl = feedProduct.ImageUrl;
            product.LastSeenUtc = now;

            product.CaloriesPer100Grams = feedProduct.CaloriesPer100Grams
                ?? ingredient?.DefaultKcalPer100g
                ?? 0;
            product.ProteinPer100Grams = feedProduct.ProteinPer100Grams
                ?? ingredient?.DefaultProteinPer100g
                ?? 0;

            // Append a price row only when something actually moved. A first sighting always
            // counts as a change so every product has at least one history row.
            var priceMoved = product.CurrentPriceObservedUtc == default
                || product.CurrentPrice != feedProduct.Price
                || product.CurrentDiscountPercent != feedProduct.DiscountPercent;

            if (priceMoved)
            {
                db.ProductPrices.Add(new ProductPrice
                {
                    Product = product,
                    Price = feedProduct.Price,
                    IsDiscounted = feedProduct.DiscountPercent > 0,
                    DiscountPercent = feedProduct.DiscountPercent,
                    ReferencePrice = feedProduct.ReferencePrice,
                    ObservedAtUtc = now
                });

                product.CurrentPrice = feedProduct.Price;
                product.CurrentDiscountPercent = feedProduct.DiscountPercent;
                product.CurrentPriceObservedUtc = now;
                priceChanges++;
            }
            else
            {
                // Price unchanged, but we did see it — keep the freshness marker moving so the
                // catalogue is not reported as stale.
                product.CurrentPriceObservedUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await UpsertNutritionAsync(db, feedProducts, existing, cancellationToken);

        return new IngestResult(feedProducts.Count, added, priceChanges);
    }

    private static async Task UpsertNutritionAsync(
        AppDbContext db,
        IReadOnlyList<FeedProduct> feedProducts,
        Dictionary<string, Product> products,
        CancellationToken cancellationToken)
    {
        var withNutrition = feedProducts.Where(p => p.Nutrition.Count > 0).ToArray();
        if (withNutrition.Length == 0)
        {
            return;
        }

        var productIds = withNutrition
            .Select(p => products[p.ExternalId].Id)
            .ToHashSet();

        var existing = await db.ProductNutrition
            .Where(n => productIds.Contains(n.ProductId))
            .ToListAsync(cancellationToken);

        var byKey = existing.ToDictionary(
            n => (n.ProductId, n.Code),
            n => n);

        foreach (var feedProduct in withNutrition)
        {
            var productId = products[feedProduct.ExternalId].Id;

            foreach (var nutrient in feedProduct.Nutrition)
            {
                if (byKey.TryGetValue((productId, nutrient.Code), out var row))
                {
                    row.Amount = nutrient.Amount;
                    row.Unit = nutrient.Unit;
                }
                else
                {
                    var added = new ProductNutrition
                    {
                        ProductId = productId,
                        Code = nutrient.Code,
                        Amount = nutrient.Amount,
                        Unit = nutrient.Unit
                    };
                    db.ProductNutrition.Add(added);
                    byKey[(productId, nutrient.Code)] = added;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
