namespace ExamApp.Api.Services.Classifier;

/// <summary>
/// Config for building the Gemini cached content that holds the classification
/// taxonomy. Bound from the "Gemini" section. Mirrors BadgeService's GeminiOptions
/// (same section name / keys) so a single set of values can be shared in dev.
/// </summary>
public class GeminiCacheOptions
{
    public const string SectionName = "Gemini";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Must match the model BadgeService calls generateContent with.</summary>
    public string Model { get; set; } = "models/gemini-3.5-flash-lite";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Cached-content lifetime. Refreshed well before this by re-running the build.</summary>
    public string Ttl { get; set; } = "604800s"; // 7 days

    public int TimeoutSeconds { get; set; } = 60;
}
