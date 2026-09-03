namespace Handlevett.Services;

/// <summary>
/// The weights behind the recommendation score. These used to be literals inside a static method
/// — the numbers that decide what the app tells you to eat, with no way to tune, test or explain
/// them. Bound from the "Scoring" configuration section; the defaults reproduce the original
/// formula exactly.
/// </summary>
public sealed class ScoringWeights
{
    public const string SectionName = "Scoring";

    /// <summary>Reward for coming in under budget. Scaled by how much of the budget is left.</summary>
    public decimal Budget { get; set; } = 40m;

    /// <summary>Reward for landing near the calorie target.</summary>
    public decimal Calories { get; set; } = 22m;

    /// <summary>Reward per average discount percentage point across the recipe's ingredients.</summary>
    public decimal Discount { get; set; } = 0.8m;

    /// <summary>Reward for total protein, scaled against <see cref="ProteinTargetGrams"/>.</summary>
    public decimal Protein { get; set; } = 18m;

    /// <summary>Penalty per unit of budget overrun. The only negative term.</summary>
    public decimal OverBudgetPenalty { get; set; } = 35m;

    /// <summary>Protein total treated as "full marks" when the high-protein preference is on.</summary>
    public decimal ProteinTargetGrams { get; set; } = 120m;

    /// <summary>Flat protein factor applied when the user has not asked for high protein.</summary>
    public decimal NeutralProteinFactor { get; set; } = 0.35m;

    /// <summary>Bonus when a recipe is tagged with a preference the user asked for.</summary>
    public decimal MealPrepBonus { get; set; } = 8m;

    public decimal QuickMealBonus { get; set; } = 8m;

    public decimal HighProteinBonus { get; set; } = 8m;
}
