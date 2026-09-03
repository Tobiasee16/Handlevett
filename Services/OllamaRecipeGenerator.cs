using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Handlevett.Models;
using Microsoft.Extensions.Options;

namespace Handlevett.Services;

/// <summary>
/// Generates recipes with a local Ollama model.
/// <para>
/// Asynchronous throughout — it used to block a request thread for up to the full 120-second
/// timeout via <c>.GetAwaiter().GetResult()</c>. Results are persisted as ordinary recipe rows
/// keyed by a generation key, replacing the static in-memory cache and the JSON blob table.
/// </para>
/// </summary>
public sealed class OllamaRecipeGenerator(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    AppRuntimeStatus runtimeStatus,
    DatabaseRecipeProvider recipeRepository,
    ILogger<OllamaRecipeGenerator> logger) : IRecipeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Long enough to span a working session, short enough that a day's price changes produce a
    /// fresh set of recipes.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(8);

    public async Task<IReadOnlyList<MealRecipe>> GenerateRecipesAsync(
        PlannerRequest request,
        IReadOnlyList<GroceryProduct> products,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            runtimeStatus.SetRecipes("Faste oppskrifter", "KI-genererte oppskrifter er slått av i konfigurasjonen.", 0);
            return [];
        }

        if (products.Count == 0)
        {
            runtimeStatus.SetRecipes("Faste oppskrifter", "Ingen matvarer var tilgjengelige for KI-generering.", 0);
            return [];
        }

        var ingredientOptions = products
            .GroupBy(product => product.IngredientKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(product => product.PricePerGram).First())
            .OrderBy(product => product.IngredientKey, StringComparer.Ordinal)
            .ToArray();

        var generationKey = BuildGenerationKey(request, ingredientOptions, settings);

        var cached = await recipeRepository.GetGeneratedAsync(generationKey, CacheTtl, cancellationToken);
        if (cached.Count > 0)
        {
            SetRecipeStatus(settings, ingredientOptions.Length, cached.Count, fromCache: true);
            return cached;
        }

        runtimeStatus.BeginRecipeGeneration();

        try
        {
            var recipes = await CallModelAsync(request, ingredientOptions, settings, cancellationToken);

            if (recipes.Count > 0)
            {
                await recipeRepository.SaveGeneratedAsync(recipes, generationKey, settings.Model, cancellationToken);

                // Re-read so the returned recipes carry their persisted identity, and so a
                // recipe rejected on save (unknown ingredient) is not shown as if it had been kept.
                var persisted = await recipeRepository.GetGeneratedAsync(generationKey, CacheTtl, cancellationToken);
                if (persisted.Count > 0)
                {
                    SetRecipeStatus(settings, ingredientOptions.Length, persisted.Count, fromCache: false);
                    return persisted;
                }
            }

            SetRecipeStatus(settings, ingredientOptions.Length, 0, fromCache: false);
            return [];
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama recipe generation timed out after {Seconds}s.", httpClient.Timeout.TotalSeconds);
            runtimeStatus.SetRecipes(
                "Faste oppskrifter",
                $"Ollama brukte for lang tid ({httpClient.Timeout.TotalSeconds:0} sekunder), så vi bruker faste oppskrifter.",
                0);
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ollama recipe generation failed.");
            runtimeStatus.SetRecipes(
                "Faste oppskrifter",
                "Ollama svarte ikke eller ga ugyldige data, så vi bruker faste oppskrifter.",
                0);
            return [];
        }
        finally
        {
            // Clears the "working" indicator whatever the outcome, including a timeout.
            runtimeStatus.EndRecipeGeneration();
        }
    }

    private void SetRecipeStatus(OllamaOptions settings, int ingredientCount, int recipeCount, bool fromCache)
    {
        if (recipeCount <= 0)
        {
            runtimeStatus.SetRecipes(
                "Faste oppskrifter",
                "Ollama returnerte ingen gyldige oppskrifter, så vi bruker faste oppskrifter.",
                0);
            return;
        }

        var cacheText = fromCache ? "lagrede " : string.Empty;
        runtimeStatus.SetRecipes(
            $"KI-generert ({settings.Model})",
            $"Bruker {recipeCount} {cacheText}KI-oppskrifter bygget på {ingredientCount} tilgjengelige råvarer.",
            recipeCount);
    }

    private async Task<IReadOnlyList<MealRecipe>> CallModelAsync(
        PlannerRequest request,
        IReadOnlyList<GroceryProduct> ingredientOptions,
        OllamaOptions settings,
        CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 15, 300));
        }

        var payload = new OllamaChatRequest(
            settings.Model,
            [
                new("system", BuildSystemPrompt()),
                new("user", BuildUserPrompt(request, ingredientOptions, settings.RecipeCount))
            ],
            BuildRecipeSchema(ingredientOptions, settings.RecipeCount),
            false,
            new(0.25m, 0.8m, 450));

        using var response = await httpClient.PostAsJsonAsync("api/chat", payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var chatResponse = await JsonSerializer.DeserializeAsync<OllamaChatResponse>(
            stream, JsonOptions, cancellationToken);

        var content = chatResponse?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var generated = JsonSerializer.Deserialize<GeneratedRecipeEnvelope>(content, JsonOptions);
        return ValidateRecipes(generated?.Recipes ?? [], ingredientOptions, request, settings.RecipeCount);
    }

    private static IReadOnlyList<MealRecipe> ValidateRecipes(
        IReadOnlyList<GeneratedRecipeDto> generatedRecipes,
        IReadOnlyList<GroceryProduct> ingredientOptions,
        PlannerRequest request,
        int recipeCount)
    {
        // One resolver built from the available products, replacing the old dictionary scan whose
        // result depended on iteration order.
        var resolver = new IngredientKeyResolver(
            ingredientOptions.SelectMany(product => new[]
            {
                new KeyValuePair<string, string>(product.IngredientKey, product.IngredientKey),
                new KeyValuePair<string, string>(product.Name, product.IngredientKey)
            }));

        var displayNames = ingredientOptions.ToDictionary(
            product => product.IngredientKey,
            product => IngredientDisplayNames.For(product.IngredientKey),
            StringComparer.OrdinalIgnoreCase);

        var recipes = new List<MealRecipe>();

        foreach (var generated in generatedRecipes)
        {
            if (recipes.Count >= recipeCount || string.IsNullOrWhiteSpace(generated.Title))
            {
                continue;
            }

            var ingredients = generated.Ingredients
                .Select(ingredient =>
                {
                    var key = resolver.Resolve(ingredient.IngredientKey);

                    return key is null
                        ? null
                        : new RecipeIngredient(
                            key,
                            displayNames.GetValueOrDefault(key, IngredientDisplayNames.For(key)),
                            NormalizeGrams(key, ingredient.Grams));
                })
                .OfType<RecipeIngredient>()
                .GroupBy(ingredient => ingredient.IngredientKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First() with { Grams = group.Sum(ingredient => ingredient.Grams) })
                .ToArray();

            if (ingredients.Length < 2)
            {
                continue;
            }

            var tags = generated.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag is "high-protein" or "vegetarian" or "meal-prep" or "quick" or "cheap")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (request.Vegetarian)
            {
                tags.Add("vegetarian");
            }

            var steps = generated.Steps
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .Select(step => step.Trim())
                .Take(8)
                .ToArray();

            recipes.Add(new MealRecipe(
                $"{Slugify(generated.Title)}-{recipes.Count + 1}",
                generated.Title.Trim(),
                string.IsNullOrWhiteSpace(generated.Description)
                    ? "Satt sammen av råvarene som er tilgjengelige nå."
                    : generated.Description.Trim(),
                ingredients,
                tags,
                Math.Clamp(generated.PrepMinutes, 10, 75),
                Math.Clamp(generated.Servings, 1, 6),
                steps.Length > 0 ? steps : BuildFallbackSteps(ingredients)));
        }

        return recipes;
    }

    private static string BuildSystemPrompt() =>
        """
        You are a practical Norwegian budget meal planner.
        Generate realistic meals using only the ingredientKey values provided by the user.
        Do not invent ingredient keys. Do not include spices, oil, water, salt, or pantry extras as ingredients.
        Prefer cheap, filling, simple meals with sensible gram amounts for the whole recipe.

        Write the title, description and steps in NORWEGIAN (bokmål) — these are shown
        directly to a Norwegian user. The ingredientKey values stay in English exactly as
        given; they are internal identifiers, not display text.

        Return only valid JSON matching the schema.
        """;

    private static string BuildUserPrompt(
        PlannerRequest request,
        IReadOnlyList<GroceryProduct> ingredientOptions,
        int recipeCount)
    {
        var allowedKeys = string.Join(", ", ingredientOptions.Select(product => product.IngredientKey));
        var ingredientLines = ingredientOptions.Select(product =>
            $"- ingredientKey={product.IngredientKey}; bestMatch=\"{product.Name}\"; category={product.Category}; price={product.PricePer100Grams:0.0} kr/100g; protein={product.ProteinPer100Grams:0} g/100g");

        var selectionNote = request.PinnedIngredients.Count > 0
            ? $"\nIMPORTANT: The user has chosen these specific ingredients. Build recipes using as many of them as possible:\n{string.Join(", ", request.PinnedIngredients)}\n"
            : string.Empty;

        return $"""
            Generate {recipeCount} recipe candidates for this meal planner.
            {selectionNote}
            User constraints:
            - Budget: {request.BudgetNok:0} NOK for the whole recipe
            - Target calories: about {request.TargetCalories:0} kcal for the whole recipe
            - Vegetarian: {request.Vegetarian}
            - High protein preferred: {request.HighProtein}
            - Meal prep preferred: {request.MealPrep}
            - Quick meals preferred: {request.QuickMeals}
            - Preferred store: {request.PreferredStore}

            Allowed ingredient keys. Use these exact strings for ingredientKey:
            {allowedKeys}

            Product reference for each key:
            {string.Join('\n', ingredientLines)}

            Rules:
            - Use 2 to 7 ingredients per recipe.
            - Every ingredient must use an ingredientKey from the allowed key list exactly.
            - Never put product names in ingredientKey. Use chicken, not Solvinge kyllingfilet 700 g.
            - Use grams for total recipe amount, not per serving.
            - If the ingredient is eggs, convert egg counts to grams before returning JSON.
            - Servings must be 1 to 6.
            - Prep minutes must be realistic.
            - Include 2 to 4 short cooking steps, written in Norwegian.
            - Tags can include high-protein, vegetarian, meal-prep, quick, cheap.
            - Example ingredient entry: ingredientKey=chicken, grams=450
            """;
    }

    private static int NormalizeGrams(string ingredientKey, int grams)
    {
        // Models routinely return "3" for eggs meaning three eggs, not three grams.
        if (ingredientKey.Equals("eggs", StringComparison.OrdinalIgnoreCase) && grams is > 0 and <= 24)
        {
            return Math.Clamp(grams * 60, 60, 1800);
        }

        return Math.Clamp(grams, 20, 1800);
    }

    private static IReadOnlyList<string> BuildFallbackSteps(IReadOnlyList<RecipeIngredient> ingredients)
    {
        var list = string.Join(", ", ingredients.Select(ingredient => ingredient.DisplayName.ToLowerInvariant()));

        return
        [
            $"Mål opp og gjør klar {list}.",
            "Kok eller stek hovedingrediensene til alt er gjennomvarmt og har god konsistens.",
            "Del opp i porsjoner og server varmt."
        ];
    }

    private static object BuildRecipeSchema(IReadOnlyList<GroceryProduct> ingredientOptions, int recipeCount)
    {
        var allowedIngredientKeys = ingredientOptions
            .Select(product => product.IngredientKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new
        {
            type = "object",
            properties = new
            {
                recipes = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = recipeCount,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                            description = new { type = "string" },
                            servings = new { type = "integer" },
                            prepMinutes = new { type = "integer" },
                            ingredients = new
                            {
                                type = "array",
                                minItems = 2,
                                maxItems = 7,
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        ingredientKey = new { type = "string", @enum = allowedIngredientKeys },
                                        grams = new { type = "integer" }
                                    },
                                    required = new[] { "ingredientKey", "grams" }
                                }
                            },
                            steps = new
                            {
                                type = "array",
                                minItems = 2,
                                maxItems = 4,
                                items = new { type = "string" }
                            },
                            tags = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            }
                        },
                        required = new[] { "title", "description", "servings", "prepMinutes", "ingredients", "steps", "tags" }
                    }
                }
            },
            required = new[] { "recipes" }
        };
    }

    /// <summary>
    /// Identifies the inputs a set of generated recipes came from. Only the vegetarian flag,
    /// the ingredient prices and the model settings matter — budget and the other preferences
    /// affect scoring, not generation, so tweaking a slider must not trigger a fresh model call.
    /// Hashed so it fits an indexed column.
    /// </summary>
    private static string BuildGenerationKey(
        PlannerRequest request,
        IReadOnlyList<GroceryProduct> ingredientOptions,
        OllamaOptions settings)
    {
        var builder = new StringBuilder()
            .Append(settings.Model).Append('|')
            .Append(settings.RecipeCount).Append('|')
            .Append(request.Vegetarian).Append('|')
            .Append(string.Join(',', request.PinnedIngredients.Order(StringComparer.Ordinal))).Append('|');

        foreach (var product in ingredientOptions)
        {
            builder.Append(product.IngredientKey).Append(':')
                   .Append(product.PackagePrice.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                   .Append(product.Store).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static string Slugify(string value)
    {
        var characters = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        var slug = string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
        return slug.Length == 0 ? "oppskrift" : slug;
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        object Format,
        bool Stream,
        OllamaRequestOptions Options);

    private sealed record OllamaRequestOptions(
        [property: JsonPropertyName("temperature")] decimal Temperature,
        [property: JsonPropertyName("top_p")] decimal TopP,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaChatResponse(OllamaMessage? Message);

    private sealed record GeneratedRecipeEnvelope(IReadOnlyList<GeneratedRecipeDto> Recipes);

    private sealed record GeneratedRecipeDto(
        string Title,
        string Description,
        int Servings,
        int PrepMinutes,
        IReadOnlyList<GeneratedIngredientDto> Ingredients,
        IReadOnlyList<string> Steps,
        IReadOnlyList<string> Tags);

    private sealed record GeneratedIngredientDto(string IngredientKey, int Grams);
}
