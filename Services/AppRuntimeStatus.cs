namespace Handlevett.Services;

/// <summary>
/// Where the app currently is in its background warm-up. Surfaced to the browser
/// through <c>/api/status</c> so a cold start shows progress instead of an
/// apparently broken page.
/// </summary>
public enum WarmupPhase
{
    NotStarted,
    FetchingPrices,
    GeneratingRecipes,
    Ready,
    Failed
}

public sealed class AppRuntimeStatus
{
    private readonly Lock sync = new();
    private RuntimeState groceryPrices = new("Venter", "Venter på første prisoppslag.", 0);
    private RuntimeState recipes = new("Venter", "Venter på første oppskriftskjøring.", 0);
    private WarmupPhase phase = WarmupPhase.NotStarted;
    private DateTime? recipeGenerationStartedUtc;

    public RuntimeState GroceryPrices
    {
        get
        {
            lock (sync)
            {
                return groceryPrices;
            }
        }
    }

    public RuntimeState Recipes
    {
        get
        {
            lock (sync)
            {
                return recipes;
            }
        }
    }

    public WarmupPhase Phase
    {
        get
        {
            lock (sync)
            {
                return phase;
            }
        }
    }

    /// <summary>
    /// True while a model call is actually in flight. Generation can take minutes on local
    /// hardware, so the UI needs to say it is working rather than appear to have stalled.
    /// </summary>
    public bool IsGeneratingRecipes
    {
        get
        {
            lock (sync)
            {
                return recipeGenerationStartedUtc is not null;
            }
        }
    }

    /// <summary>How long the current model call has been running, or zero when none is.</summary>
    public TimeSpan RecipeGenerationElapsed
    {
        get
        {
            lock (sync)
            {
                return recipeGenerationStartedUtc is null
                    ? TimeSpan.Zero
                    : DateTime.UtcNow - recipeGenerationStartedUtc.Value;
            }
        }
    }

    public void BeginRecipeGeneration()
    {
        lock (sync)
        {
            recipeGenerationStartedUtc = DateTime.UtcNow;
        }
    }

    public void EndRecipeGeneration()
    {
        lock (sync)
        {
            recipeGenerationStartedUtc = null;
        }
    }

    public void SetGroceryPrices(string summary, string detail, int itemCount)
    {
        lock (sync)
        {
            groceryPrices = new RuntimeState(summary, detail, itemCount);
        }
    }

    public void SetRecipes(string summary, string detail, int itemCount)
    {
        lock (sync)
        {
            recipes = new RuntimeState(summary, detail, itemCount);
        }
    }

    public void SetPhase(WarmupPhase value)
    {
        lock (sync)
        {
            phase = value;
        }
    }

    public sealed record RuntimeState(string Summary, string Detail, int ItemCount);
}
