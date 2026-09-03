namespace Handlevett.Data;

// ─────────────────────────────────────────────────────────────────────────────
// Reference data
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A grocery chain or store as reported by Kassal.app.</summary>
public sealed class Store
{
    public int Id { get; set; }

    /// <summary>Display name, e.g. "KIWI". Matched case-insensitively on ingest.</summary>
    public required string Name { get; set; }

    public string? ExternalId { get; set; }

    public ICollection<Product> Products { get; } = [];
}

/// <summary>
/// A canonical ingredient. Replaces the hardcoded <c>IngredientSearches</c> array that used to
/// live inside the Kassal.app HTTP client — adding a new ingredient is now a row, not a redeploy.
/// </summary>
public sealed class Ingredient
{
    public int Id { get; set; }

    /// <summary>
    /// Stable internal identifier, e.g. "chicken". Deliberately English: it is part of the
    /// contract with the recipe generator's JSON schema and with persisted recipes.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>Norwegian display name shown in the UI, e.g. "Kyllingfilet".</summary>
    public required string NameNo { get; set; }

    /// <summary>The query sent to Kassal.app when looking for products, e.g. "kyllingfilet".</summary>
    public required string SearchTerm { get; set; }

    /// <summary>protein | staple | vegetable | dairy | sauce</summary>
    public required string Category { get; set; }

    /// <summary>Used when a product carries no nutrition data of its own.</summary>
    public decimal DefaultKcalPer100g { get; set; }

    public decimal DefaultProteinPer100g { get; set; }

    /// <summary>Lets an ingredient be retired from ingest without deleting its history.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<IngredientAlias> Aliases { get; } = [];

    public ICollection<Product> Products { get; } = [];
}

/// <summary>
/// An alternative spelling that resolves to an ingredient. Drives both product normalisation
/// and the resolution of ingredient keys returned by the recipe generator, replacing the
/// order-dependent substring scan that used to decide those matches.
/// </summary>
public sealed class IngredientAlias
{
    public int Id { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    /// <summary>Normalised to lowercase alphanumerics — see <c>IngredientKeyResolver.Normalize</c>.</summary>
    public required string Alias { get; set; }

    /// <summary>seed | product-name | manual</summary>
    public required string Source { get; set; }

    public double Confidence { get; set; } = 1.0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Catalogue and price history
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A grocery product, deduplicated on <see cref="ExternalId"/> and persisted across ingest runs.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    /// <summary>Kassal.app product id or EAN. Unique.</summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? Brand { get; set; }

    public int StoreId { get; set; }

    public Store? Store { get; set; }

    /// <summary>
    /// Null until the product has been normalised to a canonical ingredient. Nullable on purpose:
    /// an unrecognised product is still worth storing, and can be matched later.
    /// </summary>
    public int? IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    public int PackageGrams { get; set; }

    public decimal CaloriesPer100Grams { get; set; }

    public decimal ProteinPer100Grams { get; set; }

    public string? ImageUrl { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    // ── Current-price projection ──
    // The proposal describes a CurrentProductPrices view. SQLite has no materialised views, so
    // the same thing is kept as columns maintained by the ingest job: the read path stays a
    // plain indexed query instead of a correlated subquery per row, while ProductPrices below
    // keeps the full history.

    public decimal CurrentPrice { get; set; }

    public int CurrentDiscountPercent { get; set; }

    public DateTime CurrentPriceObservedUtc { get; set; }

    public ICollection<ProductPrice> Prices { get; } = [];

    public ICollection<ProductNutrition> Nutrition { get; } = [];
}

/// <summary>
/// One nutrient reading, mirroring the shape Kassal.app actually returns. Calories and protein
/// are additionally denormalised onto <see cref="Product"/> because scoring reads them on every
/// request; everything else lives here so it can be queried later without a schema change.
/// </summary>
public sealed class ProductNutrition
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Kassal.app nutrient code, e.g. "energi_kcal", "protein", "karbohydrater".</summary>
    public required string Code { get; set; }

    public decimal Amount { get; set; }

    public string? Unit { get; set; }
}

/// <summary>
/// One observed price. Rows are appended only when the price actually changes, which is both
/// far more compact than a row per product per day and gives real history between days.
/// </summary>
public sealed class ProductPrice
{
    public long Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public decimal Price { get; set; }

    public bool IsDiscounted { get; set; }

    public int DiscountPercent { get; set; }

    /// <summary>Highest recent price seen, used to derive the discount.</summary>
    public decimal? ReferencePrice { get; set; }

    public DateTime ObservedAtUtc { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Recipes
// ─────────────────────────────────────────────────────────────────────────────

public enum RecipeSource
{
    /// <summary>Hand-written and seeded at startup.</summary>
    Curated = 0,

    /// <summary>Produced by the local model.</summary>
    Generated = 1,

    /// <summary>Entered by a user. Not yet reachable from the UI.</summary>
    User = 2
}

/// <summary>
/// A recipe. Replaces the JSON blob the generator used to persist, so generated recipes are
/// queryable, deduplicable and can be filtered in the database rather than in memory.
/// </summary>
public sealed class Recipe
{
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public int Servings { get; set; }

    public int PrepMinutes { get; set; }

    public RecipeSource Source { get; set; }

    public string? GeneratedByModel { get; set; }

    /// <summary>
    /// Deterministic key describing the model, settings and ingredient prices a generated recipe
    /// was produced from. Null for curated recipes. Used to decide whether generation can be
    /// skipped, replacing the separate cache table.
    /// </summary>
    public string? GenerationKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RecipeIngredientRow> Ingredients { get; } = [];

    public ICollection<RecipeStep> Steps { get; } = [];

    public ICollection<RecipeTag> Tags { get; } = [];
}

public sealed class RecipeIngredientRow
{
    public int Id { get; set; }

    public int RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    public int Grams { get; set; }
}

public sealed class RecipeStep
{
    public int Id { get; set; }

    public int RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    /// <summary>1-based position. Order is data, not a position in a serialised array.</summary>
    public int Ordinal { get; set; }

    public required string Text { get; set; }
}

public sealed class RecipeTag
{
    public int Id { get; set; }

    public int RecipeId { get; set; }

    public Recipe? Recipe { get; set; }

    /// <summary>high-protein | vegetarian | meal-prep | quick | cheap</summary>
    public required string Tag { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Operations
// ─────────────────────────────────────────────────────────────────────────────

public enum IngestStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>One execution of the grocery ingest job. Gives the scrape observability it had none of.</summary>
public sealed class IngestRun
{
    public int Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public IngestStatus Status { get; set; }

    /// <summary>Products returned by the feed, including ones that were already known.</summary>
    public int ProductsSeen { get; set; }

    /// <summary>Products written for the first time.</summary>
    public int ProductsAdded { get; set; }

    /// <summary>Products whose price differed from the last observation.</summary>
    public int PricesChanged { get; set; }

    public string? Error { get; set; }
}
