namespace Handlevett.Services;

public sealed class KassalappOptions
{
    public const string SectionName = "Kassalapp";

    public string BaseUrl { get; set; } = "https://kassal.app/api/v1/";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public bool UseLivePrices { get; set; }
}
