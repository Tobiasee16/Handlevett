namespace Handlevett.Services;

/// <summary>Settings for the background grocery ingest job.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>Run an ingest shortly after startup when the catalogue is stale or empty.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>How often the job re-checks prices. Grocery prices change at most daily.</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>
    /// Skip the startup run when the newest price observation is younger than this. Stops
    /// <c>dotnet watch</c> from re-scraping Kassal.app on every file save.
    /// </summary>
    public int SkipIfFresherThanMinutes { get; set; } = 60;

    /// <summary>
    /// Pause between searches. Kassal.app's free tier is documented at 60 calls per minute, and
    /// the job issues one search per active ingredient.
    /// </summary>
    public int DelayBetweenSearchesMs { get; set; } = 1000;
}
