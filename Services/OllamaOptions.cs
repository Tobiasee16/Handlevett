namespace Handlevett.Services;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:11434/";

    public string Model { get; set; } = "llama3.2";

    public int TimeoutSeconds { get; set; } = 120;

    public int RecipeCount { get; set; } = 2;
}
