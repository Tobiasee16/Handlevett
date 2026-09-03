using System.ComponentModel.DataAnnotations;

namespace Handlevett.Models;

public sealed class PlannerRequest
{
    [Range(40, 2000)]
    public decimal BudgetNok { get; set; } = 220;

    [Range(300, 5000)]
    public int TargetCalories { get; set; } = 1800;

    public bool Vegetarian { get; set; }

    public bool HighProtein { get; set; } = true;

    public bool MealPrep { get; set; } = true;

    public bool QuickMeals { get; set; }

    public string PreferredStore { get; set; } = "Any";

    /// <summary>
    /// When non-empty, only products with these ingredient keys are eligible for recipe matching.
    /// Populated from the Products page "Plan with selected" action.
    /// <para>
    /// Declared as <see cref="List{T}"/> rather than <see cref="IReadOnlyList{T}"/> so that model
    /// binding can round-trip it on form posts — the default collection binder cannot construct a
    /// read-only interface, which silently dropped the pinned selection when re-planning.
    /// </para>
    /// </summary>
    public List<string> PinnedIngredients { get; set; } = [];
}
