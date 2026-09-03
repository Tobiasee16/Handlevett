using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Handlevett.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogueSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductsSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductsAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    PricesChanged = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    NameNo = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SearchTerm = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DefaultKcalPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    DefaultProteinPer100g = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Servings = table.Column<int>(type: "INTEGER", nullable: false),
                    PrepMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedByModel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    GenerationKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngredientAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IngredientId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientAliases_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    IngredientId = table.Column<int>(type: "INTEGER", nullable: false),
                    Grams = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeSteps_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeTags_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    IngredientId = table.Column<int>(type: "INTEGER", nullable: true),
                    PackageGrams = table.Column<int>(type: "INTEGER", nullable: false),
                    CaloriesPer100Grams = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ProteinPer100Grams = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    CurrentDiscountPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPriceObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Products_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductNutrition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductNutrition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductNutrition_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductPrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    IsDiscounted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscountPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestRuns_StartedAtUtc",
                table: "IngestRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAliases_Alias",
                table: "IngredientAliases",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAliases_IngredientId",
                table: "IngredientAliases",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Key",
                table: "Ingredients",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductNutrition_ProductId_Code",
                table: "ProductNutrition",
                columns: new[] { "ProductId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId_ObservedAtUtc",
                table: "ProductPrices",
                columns: new[] { "ProductId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ExternalId",
                table: "Products",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_IngredientId",
                table: "Products",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId",
                table: "Products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_IngredientId",
                table: "RecipeIngredients",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_RecipeId_IngredientId",
                table: "RecipeIngredients",
                columns: new[] { "RecipeId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Slug",
                table: "Recipes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Source_GenerationKey",
                table: "Recipes",
                columns: new[] { "Source", "GenerationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_RecipeId_Ordinal",
                table: "RecipeSteps",
                columns: new[] { "RecipeId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTags_RecipeId_Tag",
                table: "RecipeTags",
                columns: new[] { "RecipeId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Name",
                table: "Stores",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestRuns");

            migrationBuilder.DropTable(
                name: "IngredientAliases");

            migrationBuilder.DropTable(
                name: "ProductNutrition");

            migrationBuilder.DropTable(
                name: "ProductPrices");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "RecipeTags");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
