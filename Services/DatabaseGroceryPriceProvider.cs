using Handlevett.Data;
using Handlevett.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Handlevett.Services;

/// <summary>
/// Serves grocery prices from the catalogue in the database.
/// <para>
/// This is the change that takes the upstream API off the request path: a page load now runs one
/// indexed query against local SQLite instead of triggering up to twenty-one HTTP calls to
/// Kassal.app on a blocked thread.
/// </para>
/// <para>
/// Caching uses <see cref="IMemoryCache"/> rather than the <c>private static</c> fields with
/// hand-rolled locks this replaced. The TTL is short because the query is already cheap — the
/// cache exists to collapse the repeated reads within a single page render, not to hide latency.
/// </para>
/// </summary>
public sealed class DatabaseGroceryPriceProvider(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IMemoryCache cache,
    AppRuntimeStatus runtimeStatus,
    ILogger<DatabaseGroceryPriceProvider> logger) : IGroceryPriceProvider
{
    private const string CacheKey = "grocery-products";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>Beyond this age the catalogue is reported as stale in the UI.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(36);

    public async Task<IReadOnlyList<GroceryProduct>> GetCurrentProductsAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<GroceryProduct>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Only normalised products can be priced against a recipe, so the filter is also what
            // the IngredientId index exists for.
            var rows = await db.Products
                .AsNoTracking()
                .Where(p => p.IngredientId != null && p.CurrentPrice > 0 && p.PackageGrams > 0)
                .Select(p => new
                {
                    p.ExternalId,
                    IngredientKey = p.Ingredient!.Key,
                    p.Name,
                    StoreName = p.Store!.Name,
                    p.CurrentPrice,
                    p.PackageGrams,
                    p.CurrentDiscountPercent,
                    p.CaloriesPer100Grams,
                    p.ProteinPer100Grams,
                    Category = p.Ingredient!.Category,
                    p.CurrentPriceObservedUtc
                })
                .ToListAsync(cancellationToken);

            var products = rows
                .Select(r => new GroceryProduct(
                    r.ExternalId,
                    r.IngredientKey,
                    r.Name,
                    r.StoreName,
                    r.CurrentPrice,
                    r.PackageGrams,
                    r.CurrentDiscountPercent,
                    r.CaloriesPer100Grams,
                    r.ProteinPer100Grams,
                    r.Category))
                .ToArray();

            ReportStatus(products.Length, rows.Count > 0 ? rows.Max(r => r.CurrentPriceObservedUtc) : null);

            cache.Set(CacheKey, (IReadOnlyList<GroceryProduct>)products, CacheTtl);
            return products;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read the grocery catalogue from the database.");
            runtimeStatus.SetGroceryPrices("Databasefeil", "Klarte ikke lese varekatalogen fra databasen.", 0);
            return [];
        }
    }

    /// <summary>Drops the cached catalogue so the next read reflects a completed ingest run.</summary>
    public void Invalidate() => cache.Remove(CacheKey);

    private void ReportStatus(int count, DateTime? newestObservationUtc)
    {
        if (count == 0)
        {
            runtimeStatus.SetGroceryPrices(
                "Ingen varer",
                "Varekatalogen er tom. Kjør en innhenting, eller legg inn en Kassal.app-nøkkel.",
                0);
            return;
        }

        var age = newestObservationUtc is null
            ? TimeSpan.MaxValue
            : DateTime.UtcNow - newestObservationUtc.Value;

        if (age > StaleAfter)
        {
            runtimeStatus.SetGroceryPrices(
                "Utdaterte priser",
                $"{count} varer i katalogen, men prisene er {FormatAge(age)} gamle.",
                count);
            return;
        }

        runtimeStatus.SetGroceryPrices(
            "Live priser",
            $"{count} varer i katalogen, sist oppdatert {FormatAge(age)} siden.",
            count);
    }

    private static string FormatAge(TimeSpan age) => age switch
    {
        { TotalMinutes: < 2 } => "under et minutt",
        { TotalHours: < 1 } => $"{age.TotalMinutes:0} minutter",
        { TotalDays: < 1 } => $"{age.TotalHours:0} timer",
        _ => $"{age.TotalDays:0} dager"
    };
}
