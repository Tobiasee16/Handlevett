using Handlevett.Models;
using Microsoft.Extensions.Options;

namespace Handlevett.Services;

/// <summary>
/// Owns everything that used to happen on the request path: fetching prices, and pre-generating
/// recipes so the first page render is instant.
/// <para>
/// A <see cref="BackgroundService"/> rather than the old fire-and-forget <c>Task.Run</c> inside
/// <c>StartAsync</c>, so it participates in graceful shutdown and re-runs on a schedule instead
/// of only once at boot.
/// </para>
/// </summary>
public sealed class IngestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestOptions> options,
    AppRuntimeStatus runtimeStatus,
    ILogger<IngestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromHours(Math.Clamp(settings.IntervalHours, 1, 24 * 7));

        // Let the host finish starting before touching the network or the database.
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                runtimeStatus.SetPhase(WarmupPhase.Failed);
                logger.LogWarning(ex, "Ingest cycle failed (non-fatal — the app keeps serving what it has).");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(IngestOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        var ingest = provider.GetRequiredService<GroceryIngestService>();
        var prices = provider.GetRequiredService<IGroceryPriceProvider>();
        var recommendations = provider.GetRequiredService<IMealRecommendationService>();

        // ── Prices ──
        if (settings.RunOnStartup && await ingest.IsCatalogueStaleAsync(cancellationToken))
        {
            runtimeStatus.SetPhase(WarmupPhase.FetchingPrices);
            logger.LogInformation("Ingest: catalogue is stale, fetching prices.");

            try
            {
                await ingest.RunAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed fetch is survivable: whatever is already in the catalogue still serves.
                logger.LogWarning(ex, "Ingest: price fetch failed, continuing with the stored catalogue.");
            }

            if (prices is DatabaseGroceryPriceProvider database)
            {
                database.Invalidate();
            }
        }
        else
        {
            logger.LogInformation("Ingest: catalogue is fresh, skipping the price fetch.");
        }

        // ── Recipes ──
        // Warms the generated-recipe cache so the first visitor does not wait on the model.
        runtimeStatus.SetPhase(WarmupPhase.GeneratingRecipes);
        await recommendations.RecommendAsync(new PlannerRequest(), cancellationToken);

        // ── Housekeeping ──
        var repository = provider.GetRequiredService<DatabaseRecipeProvider>();
        await repository.PruneGeneratedAsync(TimeSpan.FromDays(7), cancellationToken);

        runtimeStatus.SetPhase(WarmupPhase.Ready);
        logger.LogInformation("Ingest: cycle complete.");
    }
}
