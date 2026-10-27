namespace BadgeService.Services;

/// <summary>
/// Issue #577: how <c>QuestionCreatedConsumer</c> treats questions whose subject/topic came from a book's table of
/// contents (<c>ClassificationSource = "Book"</c>). Config <c>QuestionAnalyzer:BookTocMode</c>.
/// <list type="bullet">
/// <item><see cref="Hybrid"/> (default, also when unset): with a subtopic → no classification; with a topic but no
/// subtopic → classify only the subtopic + difficulty within that topic (topic/subject stay, source stays <c>Book</c>);
/// without a topic → classified like a human-authored question.</item>
/// <item><see cref="Skip"/>: never classify book questions.</item>
/// <item><see cref="Always"/>: classify like any human-authored question (AI result overwrites the book's topic).</item>
/// </list>
/// </summary>
public enum BookTocMode
{
    Hybrid,
    Skip,
    Always,
}

public static class BookTocModeConfig
{
    public const string Key = "QuestionAnalyzer:BookTocMode";

    /// <summary>Reads <see cref="Key"/>; empty = <see cref="BookTocMode.Hybrid"/>, unknown value throws (fail fast at startup).</summary>
    public static BookTocMode Read(IConfiguration configuration)
    {
        var value = configuration[Key];
        if (string.IsNullOrWhiteSpace(value))
            return BookTocMode.Hybrid;
        if (Enum.TryParse<BookTocMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
            return mode;
        throw new InvalidOperationException($"{Key} must be 'Hybrid', 'Skip' or 'Always' (was '{value}').");
    }
}
