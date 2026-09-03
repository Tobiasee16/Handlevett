using Handlevett.Models;
using Handlevett.Services;

namespace Handlevett.Tests;

/// <summary>
/// Two separate concerns live here: prices are displayed per kilo, and machine-readable output
/// must stay invariant. The second is easy to regress — the app runs under nb-NO, where a decimal
/// comma silently truncates in JavaScript's parseFloat.
/// </summary>
public class FormatTests
{
    private static GroceryProduct Product(decimal packagePrice, int grams) =>
        new("id", "chicken", "Kyllingfilet", "KIWI", packagePrice, grams, 0, 100, 20, "protein");

    [Fact]
    public void PricePerKiloIsTenTimesPricePer100Grams()
    {
        var product = Product(packagePrice: 89.90m, grams: 700);

        Assert.Equal(product.PricePer100Grams * 10, product.PricePerKilo);
    }

    [Fact]
    public void FormatsUnitPricePerKilo()
    {
        // 100 kr for 1 kg reads as 100,00 kr/kg — not 10,00 kr/100 g.
        var product = Product(packagePrice: 100m, grams: 1000);

        Assert.Equal("100,00 kr/kg", Format.KrPerKg(product.PricePerKilo));
    }

    [Fact]
    public void DisplayFormattingUsesTheNorwegianDecimalComma()
    {
        Assert.Equal("18,40 kr", Format.KrExact(18.4m));
        Assert.Equal("32,50 kr/kg", Format.KrPerKg(32.5m));
    }

    [Fact]
    public void MachineReadableFormattingStaysInvariant()
    {
        // This is the one that fails silently: parseFloat("32,50") returns 32, which would
        // reorder the Products page without any error.
        Assert.Equal("32.5", Format.Data(32.5m));
        Assert.Equal("0.0625", Format.Data(0.0625m));
    }

    [Fact]
    public void MachineReadableOutputRoundTripsThroughInvariantParsing()
    {
        var value = 1234.5678m;

        Assert.True(decimal.TryParse(
            Format.Data(value),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed));

        Assert.Equal(1234.5678m, parsed);
    }
}
