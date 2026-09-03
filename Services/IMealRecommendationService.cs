using Handlevett.Models;

namespace Handlevett.Services;

public interface IMealRecommendationService
{
    Task<IReadOnlyList<MealRecommendation>> RecommendAsync(
        PlannerRequest request,
        CancellationToken cancellationToken = default);
}
