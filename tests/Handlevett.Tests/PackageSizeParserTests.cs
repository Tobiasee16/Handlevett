using Handlevett.Services;

namespace Handlevett.Tests;

/// <summary>
/// The package-weight guess sets the denominator for every price-per-gram figure in the app.
/// When it is wrong nothing throws — the meal simply gets ranked against a fabricated price —
/// which is why it is worth pinning down.
/// </summary>
public class PackageSizeParserTests
{
    [Theory]
    [InlineData("Solvinge Kyllingfilet 700 g", 700)]
    [InlineData("Gilde Karbonadedeig 400g", 400)]
    [InlineData("First Price Ris 1 kg", 1000)]
    [InlineData("Jasminris 1,5 kg", 1500)]
    [InlineData("Havregryn 1.5kg", 1500)]
    [InlineData("TINE Gresk Yoghurt 350 gram", 350)]
    public void ReadsWeightFromName(string name, int expected)
    {
        Assert.Equal(expected, PackageSizeParser.ExtractGrams(name));
    }

    [Fact]
    public void MultipliesMultipacks()
    {
        // "4 x 125 g" is half a kilo, not 125 grams — the old parser took the first number it
        // recognised and reported 4 g.
        Assert.Equal(500, PackageSizeParser.ExtractGrams("Yoghurt Naturell 4 x 125 g"));
    }

    [Fact]
    public void FallsBackWhenTheNameHasNoWeight()
    {
        Assert.Equal(PackageSizeParser.FallbackGrams, PackageSizeParser.ExtractGrams("Brokkoli"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackOnEmptyInput(string? name)
    {
        Assert.Equal(PackageSizeParser.FallbackGrams, PackageSizeParser.ExtractGrams(name));
    }

    [Fact]
    public void DoesNotTreatABareNumberAsGrams()
    {
        // "2 gulrot" is a count, not two grams. Reading it as a weight would make carrots look
        // roughly 250 times more expensive per gram than they are.
        Assert.Equal(PackageSizeParser.FallbackGrams, PackageSizeParser.ExtractGrams("2 gulrot"));
    }

    [Fact]
    public void IgnoresImplausiblyLargeWeights()
    {
        Assert.Equal(PackageSizeParser.FallbackGrams, PackageSizeParser.ExtractGrams("Lagervare 900 kg"));
    }
}
