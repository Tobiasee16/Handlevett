using Microsoft.EntityFrameworkCore;

namespace Handlevett.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<IngredientAlias> IngredientAliases => Set<IngredientAlias>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductNutrition> ProductNutrition => Set<ProductNutrition>();

    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();

    public DbSet<Recipe> Recipes => Set<Recipe>();

    public DbSet<RecipeIngredientRow> RecipeIngredients => Set<RecipeIngredientRow>();

    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();

    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();

    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── Reference data ──

        model.Entity<Store>(entity =>
        {
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.Name).HasMaxLength(120);
        });

        model.Entity<Ingredient>(entity =>
        {
            entity.HasIndex(i => i.Key).IsUnique();
            entity.Property(i => i.Key).HasMaxLength(60);
            entity.Property(i => i.NameNo).HasMaxLength(120);
            entity.Property(i => i.SearchTerm).HasMaxLength(120);
            entity.Property(i => i.Category).HasMaxLength(40);
        });

        model.Entity<IngredientAlias>(entity =>
        {
            entity.HasIndex(a => a.Alias).IsUnique();
            entity.Property(a => a.Alias).HasMaxLength(160);
            entity.Property(a => a.Source).HasMaxLength(40);
            entity.HasOne(a => a.Ingredient)
                  .WithMany(i => i.Aliases)
                  .HasForeignKey(a => a.IngredientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Catalogue and prices ──

        model.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.ExternalId).IsUnique();

            // The read path filters on "normalised products" and joins the store, so index the
            // discriminating column rather than relying on a table scan of the whole catalogue.
            entity.HasIndex(p => p.IngredientId);

            entity.Property(p => p.ExternalId).HasMaxLength(120);
            entity.Property(p => p.Name).HasMaxLength(400);
            entity.Property(p => p.Brand).HasMaxLength(200);
            entity.Property(p => p.CurrentPrice).HasPrecision(10, 2);
            entity.Property(p => p.CaloriesPer100Grams).HasPrecision(8, 2);
            entity.Property(p => p.ProteinPer100Grams).HasPrecision(8, 2);

            entity.HasOne(p => p.Store)
                  .WithMany(s => s.Products)
                  .HasForeignKey(p => p.StoreId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Keep the price history if an ingredient is retired — only the link is cleared.
            entity.HasOne(p => p.Ingredient)
                  .WithMany(i => i.Products)
                  .HasForeignKey(p => p.IngredientId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ProductNutrition>(entity =>
        {
            entity.HasIndex(n => new { n.ProductId, n.Code }).IsUnique();
            entity.Property(n => n.Code).HasMaxLength(80);
            entity.Property(n => n.Unit).HasMaxLength(20);
            entity.Property(n => n.Amount).HasPrecision(10, 3);
            entity.HasOne(n => n.Product)
                  .WithMany(p => p.Nutrition)
                  .HasForeignKey(n => n.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ProductPrice>(entity =>
        {
            // Every history query is "the prices of this product, newest first".
            entity.HasIndex(p => new { p.ProductId, p.ObservedAtUtc });
            entity.Property(p => p.Price).HasPrecision(10, 2);
            entity.Property(p => p.ReferencePrice).HasPrecision(10, 2);
            entity.HasOne(p => p.Product)
                  .WithMany(p => p.Prices)
                  .HasForeignKey(p => p.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Recipes ──

        model.Entity<Recipe>(entity =>
        {
            entity.HasIndex(r => r.Slug).IsUnique();
            entity.HasIndex(r => new { r.Source, r.GenerationKey });
            entity.Property(r => r.Slug).HasMaxLength(200);
            entity.Property(r => r.Name).HasMaxLength(300);
            entity.Property(r => r.GeneratedByModel).HasMaxLength(120);
            entity.Property(r => r.GenerationKey).HasMaxLength(64);
            entity.Property(r => r.Source).HasConversion<int>();
        });

        model.Entity<RecipeIngredientRow>(entity =>
        {
            entity.HasIndex(ri => new { ri.RecipeId, ri.IngredientId }).IsUnique();
            entity.HasOne(ri => ri.Recipe)
                  .WithMany(r => r.Ingredients)
                  .HasForeignKey(ri => ri.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(ri => ri.Ingredient)
                  .WithMany()
                  .HasForeignKey(ri => ri.IngredientId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<RecipeStep>(entity =>
        {
            entity.HasIndex(s => new { s.RecipeId, s.Ordinal }).IsUnique();
            entity.Property(s => s.Text).HasMaxLength(1000);
            entity.HasOne(s => s.Recipe)
                  .WithMany(r => r.Steps)
                  .HasForeignKey(s => s.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RecipeTag>(entity =>
        {
            entity.HasIndex(t => new { t.RecipeId, t.Tag }).IsUnique();
            entity.Property(t => t.Tag).HasMaxLength(40);
            entity.HasOne(t => t.Recipe)
                  .WithMany(r => r.Tags)
                  .HasForeignKey(t => t.RecipeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Operations ──

        model.Entity<IngestRun>(entity =>
        {
            entity.HasIndex(r => r.StartedAtUtc);
            entity.Property(r => r.Status).HasConversion<int>();
            entity.Property(r => r.Error).HasMaxLength(2000);
        });
    }
}
