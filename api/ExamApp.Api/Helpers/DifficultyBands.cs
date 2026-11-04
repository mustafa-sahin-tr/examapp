namespace ExamApp.Api.Helpers;

/// <summary>Zorluk seviyesinin (1-10) bandı: öğretmen içgörüleri ve konu performansı aynı sınırları kullanır.</summary>
public static class DifficultyBands
{
    public const string Easy = "easy";
    public const string Medium = "medium";
    public const string Hard = "hard";

    public static string Of(int level) => level switch
    {
        <= 3 => Easy,
        <= 6 => Medium,
        _ => Hard,
    };
}
