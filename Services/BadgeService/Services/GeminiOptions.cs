namespace BadgeService.Services;

/// <summary>Config for the Gemini-backed question classifier. Bound from the "Gemini" section.</summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>e.g. "models/gemini-3.5-flash-lite". Must match the model the cached content was created with.</summary>
    public string Model { get; set; } = "models/gemini-3.5-flash-lite";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Name of the Gemini cached content holding the subject/topic/subtopic taxonomy,
    /// e.g. "cachedContents/npjwa19dr5vm2j9023azyxzhtvajrcclmsghxxyl".
    /// </summary>
    public string CachedContent { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}
