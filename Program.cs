using System.Globalization;
using Handlevett.Data;
using Handlevett.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<AppRuntimeStatus>();

// ── Configuration ──
builder.Services.Configure<KassalappOptions>(builder.Configuration.GetSection(KassalappOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));
builder.Services.Configure<ScoringWeights>(builder.Configuration.GetSection(ScoringWeights.SectionName));

// ── Database ──
// SQLite for local development. The schema is provider-agnostic and managed by EF Core
// migrations, so moving to PostgreSQL is a package and connection-string change.
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "handlevett.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite($"Data Source={dbPath}"));

// ── Outbound HTTP ──
// Standard resilience adds retries with jitter, a circuit breaker and per-attempt timeouts, so a
// slow or rate-limited upstream degrades instead of hanging.
builder.Services.AddHttpClient<IProductFeed, KassalappProductFeed>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<IRecipeGenerator, OllamaRecipeGenerator>();

// ── Application services ──
// Scoped, not singleton: each resolves a DbContext, and none of them hold mutable state any more.
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<GroceryIngestService>();
builder.Services.AddScoped<DatabaseRecipeProvider>();
builder.Services.AddScoped<IRecipeProvider>(sp => sp.GetRequiredService<DatabaseRecipeProvider>());
builder.Services.AddScoped<IGroceryPriceProvider, DatabaseGroceryPriceProvider>();
builder.Services.AddScoped<IMealRecommendationService, MealRecommendationService>();

// Background price ingest and recipe warm-up. Nothing on the request path talks to Kassal.app.
builder.Services.AddHostedService<IngestBackgroundService>();

var app = builder.Build();

// Apply migrations and seed reference data before the first request is served.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().MigrateAndSeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// The UI is Norwegian: prices and decimals must format as "182 kr" / "18,40 kr". Anything a
// machine reads back (data- attributes, JSON) is still written with InvariantCulture — see
// Services/Format.cs.
var norwegian = CultureInfo.GetCultureInfo("nb-NO");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(norwegian),
    SupportedCultures = [norwegian],
    SupportedUICultures = [norwegian]
});

app.UseRouting();

app.UseAuthorization();

// Polled by wwwroot/js/site.js so a cold start shows warm-up progress rather than an
// apparently broken page.
app.MapGet("/api/status", (AppRuntimeStatus status) => Results.Json(new
{
    phase = status.Phase.ToString(),
    prices = new
    {
        summary = status.GroceryPrices.Summary,
        detail = status.GroceryPrices.Detail,
        itemCount = status.GroceryPrices.ItemCount
    },
    recipes = new
    {
        summary = status.Recipes.Summary,
        detail = status.Recipes.Detail,
        itemCount = status.Recipes.ItemCount
    },
    // True while a model call is actually in flight, so the page can show that Ollama is
    // working rather than appearing to hang for up to two minutes.
    generating = status.IsGeneratingRecipes,
    generatingSeconds = (int)status.RecipeGenerationElapsed.TotalSeconds
}));

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

/// <summary>Exposed so the test project can reference the web assembly.</summary>
public partial class Program;
