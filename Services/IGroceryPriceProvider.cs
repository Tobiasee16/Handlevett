using Handlevett.Models;

namespace Handlevett.Services;

/// <summary>
/// The read path for grocery prices. Backed by the database, not by a live upstream call —
/// ingestion happens in the background so a page request never waits on Kassal.app.
/// </summary>
public interface IGroceryPriceProvider
{
    Task<IReadOnlyList<GroceryProduct>> GetCurrentProductsAsync(CancellationToken cancellationToken = default);
}
