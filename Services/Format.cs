using System.Globalization;

namespace Handlevett.Services;

/// <summary>
/// Culture-safe formatting helpers.
/// <para>
/// The app runs under nb-NO, so <c>decimal.ToString("0.00")</c> produces
/// <c>12,34</c>. That is correct for display but wrong for anything a machine
/// reads back: JavaScript's <c>parseFloat("12,34")</c> silently returns
/// <c>12</c>, which would break the Products page sorting without any error.
/// </para>
/// <para>
/// Use <see cref="Data(decimal)"/> for every value written into a <c>data-</c>
/// attribute, query string or JSON payload, and the display helpers for
/// everything a person reads.
/// </para>
/// </summary>
public static class Format
{
    private static readonly CultureInfo Display = CultureInfo.GetCultureInfo("nb-NO");

    /// <summary>Invariant, machine-readable. Always use this for data- attributes.</summary>
    public static string Data(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Invariant, machine-readable.</summary>
    public static string Data(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whole kroner, e.g. "182 kr".</summary>
    public static string Kr(decimal value) =>
        value.ToString("0", Display) + " kr";

    /// <summary>Kroner with øre, e.g. "18,40 kr".</summary>
    public static string KrExact(decimal value) =>
        value.ToString("0.00", Display) + " kr";

    /// <summary>Comparative unit price, e.g. "32,00 kr/kg". Kilo is the unit shoppers compare with.</summary>
    public static string KrPerKg(decimal value) =>
        value.ToString("0.00", Display) + " kr/kg";

    /// <summary>Whole grams, e.g. "450 g".</summary>
    public static string Grams(int value) =>
        value.ToString("N0", Display) + " g";

    /// <summary>Protein with one decimal, e.g. "62,5 g".</summary>
    public static string GramsExact(decimal value) =>
        value.ToString("0.#", Display) + " g";

    /// <summary>Energy, e.g. "1 850 kcal".</summary>
    public static string Kcal(int value) =>
        value.ToString("N0", Display) + " kcal";

    /// <summary>Energy from a decimal source, e.g. "142 kcal".</summary>
    public static string Kcal(decimal value) =>
        value.ToString("N0", Display) + " kcal";

    /// <summary>A plain rounded number in display culture, e.g. "1 850".</summary>
    public static string Number(decimal value, int decimals = 0) =>
        value.ToString("N" + decimals, Display);
}
