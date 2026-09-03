using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Handlevett.Services;

/// <summary>
/// Reads grocery products from Kassal.app.
/// <para>
/// Fully asynchronous and reached only from the background ingest job. It used to be called
/// synchronously from a Razor page via GetAwaiter().GetResult(), which blocked a thread-pool
/// thread for the whole run — one search per ingredient, each followed by a one-second pause.
/// </para>
/// </summary>
public sealed class KassalappProductFeed(
    HttpClient httpClient,
    IOptions<KassalappOptions> options,
    IOptions<IngestOptions> ingestOptions,
    ILogger<KassalappProductFeed> logger) : IProductFeed
{
    public async Task<IReadOnlyList<FeedProduct>> FetchAsync(
        IReadOnlyList<FeedQuery> queries,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.UseLivePrices)
        {
            throw new InvalidOperationException(
                "Live priser er slått av. Sett Kassalapp:UseLivePrices=true og legg inn en API-nøkkel.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Kassal.app-nøkkel mangler. Kjør: dotnet user-secrets set Kassalapp:ApiKey DIN_NØKKEL");
        }

        ConfigureClient(settings);

        var delay = TimeSpan.FromMilliseconds(Math.Max(0, ingestOptions.Value.DelayBetweenSearchesMs));
        var products = new List<FeedProduct>();

        for (var i = 0; i < queries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = queries[i];

            try
            {
                using var response = await httpClient.GetAsync(
                    $"products?search={Uri.EscapeDataString(query.SearchTerm)}",
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                products.AddRange(ParseProducts(document.RootElement, query));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed search should not abandon the other twenty.
                logger.LogWarning(ex, "Feed search failed for {Ingredient}.", query.IngredientKey);
            }

            if (i < queries.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        // The same product can be returned by more than one search.
        return [.. products.GroupBy(p => p.ExternalId).Select(g => g.First())];
    }

    private void ConfigureClient(KassalappOptions settings)
    {
        if (httpClient.BaseAddress is null)
        {
            httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 3, 30));
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Handlevett/0.2");
    }

    private static IEnumerable<FeedProduct> ParseProducts(JsonElement root, FeedQuery query)
    {
        foreach (var item in EnumerateProductElements(root))
        {
            var name = ReadString(item, "name", "product_name", "title") ?? query.SearchTerm;
            var price = ReadDecimal(item, "current_price", "price", "price_current", "unit_price") ?? 0;
            var grams = ReadPackageGrams(item, name);

            if (price <= 0 || grams <= 0)
            {
                continue;
            }

            var (discountPercent, referencePrice) = ReadDiscount(item, price);

            yield return new FeedProduct(
                ReadString(item, "id", "product_id", "ean") ?? $"{query.IngredientKey}-{name}",
                name,
                ReadString(item, "brand", "vendor"),
                ReadStore(item),
                query.IngredientKey,
                price,
                grams,
                discountPercent,
                referencePrice,
                ReadNutrition(item, "energi_kcal", "calories", "energy_kcal"),
                ReadNutrition(item, "protein", "proteins"),
                ReadString(item, "image", "image_url", "thumbnail"),
                ReadAllNutrition(item));
        }
    }

    private static IEnumerable<JsonElement> EnumerateProductElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }
        }

        foreach (var propertyName in new[] { "data", "products", "items", "results" })
        {
            if (!root.TryGetProperty(propertyName, out var child))
            {
                continue;
            }

            if (child.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in child.EnumerateArray())
                {
                    yield return item;
                }
            }
            else if (child.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in EnumerateProductElements(child))
                {
                    yield return item;
                }
            }
        }
    }

    private static string ReadStore(JsonElement item)
    {
        if (TryGetProperty(item, "store", out var store))
        {
            return ReadString(store, "name", "chain", "store") ?? "Ukjent butikk";
        }

        if (TryGetProperty(item, "stores", out var stores) && stores.ValueKind == JsonValueKind.Array)
        {
            foreach (var storeItem in stores.EnumerateArray())
            {
                return ReadString(storeItem, "name", "chain", "store") ?? "Ukjent butikk";
            }
        }

        return ReadString(item, "store_name", "vendor", "chain") ?? "Ukjent butikk";
    }

    /// <summary>Returns the discount percentage and, where derivable, the price it is measured against.</summary>
    private static (int Percent, decimal? Reference) ReadDiscount(JsonElement item, decimal currentPrice)
    {
        var discount = ReadDecimal(item, "discount", "discount_percent", "discountPercentage");

        if (discount is null && TryGetProperty(item, "price", out var priceElement))
        {
            discount = ReadDecimal(priceElement, "discount", "discount_percent", "discountPercentage");
        }

        decimal? reference = null;

        if (discount is null
            && TryGetProperty(item, "price_history", out var history)
            && history.ValueKind == JsonValueKind.Array)
        {
            decimal? highest = null;
            var seen = 0;

            foreach (var entry in history.EnumerateArray())
            {
                var historic = ReadDecimal(entry, "price");
                if (historic is null)
                {
                    continue;
                }

                if (highest is null || historic > highest)
                {
                    highest = historic;
                }

                if (++seen >= 10)
                {
                    break;
                }
            }

            if (highest is not null && highest > currentPrice && currentPrice > 0)
            {
                reference = highest;
                discount = Math.Round((highest.Value - currentPrice) / highest.Value * 100, 0);
            }
        }

        return (Math.Clamp((int)Math.Round(discount ?? 0), 0, 95), reference);
    }

    private static IReadOnlyList<FeedNutrient> ReadAllNutrition(JsonElement item)
    {
        foreach (var property in new[] { "nutrition", "nutrition_info", "nutritional_content" })
        {
            if (!TryGetProperty(item, property, out var nutrition) || nutrition.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var nutrients = new List<FeedNutrient>();

            foreach (var entry in nutrition.EnumerateArray())
            {
                if (!TryGetProperty(entry, "code", out var codeElement))
                {
                    continue;
                }

                var code = codeElement.GetString();
                var amount = ReadDecimal(entry, "amount");

                if (!string.IsNullOrWhiteSpace(code) && amount is not null)
                {
                    nutrients.Add(new FeedNutrient(code, amount.Value, ReadString(entry, "unit")));
                }
            }

            if (nutrients.Count > 0)
            {
                return nutrients;
            }
        }

        return [];
    }

    private static decimal? ReadNutrition(JsonElement item, params string[] codes)
    {
        foreach (var property in new[] { "nutrition", "nutrition_info", "nutritional_content" })
        {
            if (!TryGetProperty(item, property, out var nutrition))
            {
                continue;
            }

            if (nutrition.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in nutrition.EnumerateArray())
                {
                    if (!TryGetProperty(entry, "code", out var codeElement))
                    {
                        continue;
                    }

                    var code = codeElement.GetString();

                    if (codes.Any(target => string.Equals(code, target, StringComparison.OrdinalIgnoreCase)))
                    {
                        return ReadDecimal(entry, "amount");
                    }
                }
            }
            else
            {
                var value = ReadDecimal(nutrition, codes);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return ReadDecimal(item, codes);
    }

    private static int ReadPackageGrams(JsonElement item, string name)
    {
        var grams = ReadDecimal(item, "weight", "weight_grams", "net_content", "amount");

        if (grams is not null && TryGetProperty(item, "weight_unit", out var unitElement))
        {
            var unit = unitElement.GetString() ?? string.Empty;
            if (unit.Equals("kg", StringComparison.OrdinalIgnoreCase))
            {
                grams *= 1000;
            }
        }

        return grams is > 0
            ? (int)Math.Max(1, Math.Round(grams.Value))
            : PackageSizeParser.ExtractGrams(name);
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(item, name, out var property))
            {
                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(item, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String
                && decimal.TryParse(
                    property.GetString()?.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
