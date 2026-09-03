using Handlevett.Services;

namespace Handlevett.Tests;

/// <summary>
/// The resolver decides which grocery product a model-generated ingredient key refers to. The
/// implementation it replaced scanned a dictionary with <c>Contains</c> and returned the first
/// hit, so the answer depended on insertion order and changed as the product list changed.
/// </summary>
public class IngredientKeyResolverTests
{
    private static IngredientKeyResolver Build() => new(
    [
        new("chicken", "chicken"),
        new("Kyllingfilet", "chicken"),
        new("Solvinge Kyllingfilet 700 g", "chicken"),
        new("rice", "rice"),
        new("Ris", "rice"),
        new("cottage-cheese", "cottage-cheese"),
        new("Cottage cheese", "cottage-cheese"),
        new("peanut-butter", "peanut-butter"),
        new("Peanøttsmør", "peanut-butter")
    ]);

    [Fact]
    public void ResolvesACanonicalKeyUnchanged()
    {
        Assert.Equal("chicken", Build().Resolve("chicken"));
    }

    [Theory]
    [InlineData("Kyllingfilet", "chicken")]
    [InlineData("kyllingfilet", "chicken")]
    [InlineData("KYLLINGFILET", "chicken")]
    [InlineData("Peanøttsmør", "peanut-butter")]
    [InlineData("Cottage Cheese", "cottage-cheese")]
    public void ResolvesAliasesRegardlessOfCasing(string input, string expected)
    {
        Assert.Equal(expected, Build().Resolve(input));
    }

    [Fact]
    public void ResolvesAKeyEmbeddedInAProductName()
    {
        Assert.Equal("chicken", Build().Resolve("Solvinge Kyllingfilet 700 g"));
    }

    [Fact]
    public void IgnoresPunctuationAndSpacing()
    {
        Assert.Equal("peanut-butter", Build().Resolve("peanut butter"));
        Assert.Equal("cottage-cheese", Build().Resolve("cottage_cheese"));
    }

    [Fact]
    public void ReturnsNullForSomethingItDoesNotStock()
    {
        Assert.Null(Build().Resolve("kokosmelk"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("!!!")]
    public void ReturnsNullForEmptyInput(string? input)
    {
        Assert.Null(Build().Resolve(input));
    }

    [Fact]
    public void DoesNotMatchOnVeryShortFragments()
    {
        // "ri" must not latch onto "rice" — a two-character fragment is not evidence.
        Assert.Null(Build().Resolve("ri"));
    }

    [Fact]
    public void IsDeterministicAcrossDifferentInsertionOrders()
    {
        // The behaviour the old substring scan could not guarantee: the same input resolves to
        // the same ingredient no matter what order the aliases were registered in.
        var forward = new IngredientKeyResolver(
        [
            new("Kyllingfilet", "chicken"),
            new("Kylling", "chicken"),
            new("Kyllingfilet Strimlet", "chicken-strips")
        ]);

        var reversed = new IngredientKeyResolver(
        [
            new("Kyllingfilet Strimlet", "chicken-strips"),
            new("Kylling", "chicken"),
            new("Kyllingfilet", "chicken")
        ]);

        Assert.Equal(forward.Resolve("Prior Kyllingfilet 400 g"), reversed.Resolve("Prior Kyllingfilet 400 g"));
    }

    [Fact]
    public void PrefersTheMoreSpecificAlias()
    {
        var resolver = new IngredientKeyResolver(
        [
            new("Kylling", "chicken"),
            new("Kyllingfilet Strimlet", "chicken-strips")
        ]);

        // Both aliases appear in the input; the longer, more specific one must win.
        Assert.Equal("chicken-strips", resolver.Resolve("Prior Kyllingfilet Strimlet 400 g"));
    }

    [Theory]
    [InlineData("Peanøttsmør 350 g", "peanøttsmør350g")]
    [InlineData("Cottage cheese", "cottagecheese")]
    [InlineData("  RIS  ", "ris")]
    public void NormalizeStripsEverythingButAlphanumerics(string input, string expected)
    {
        Assert.Equal(expected, IngredientKeyResolver.Normalize(input));
    }
}
