namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #571: çoktan seçmeli — bugünkü kural birebir: seçilen şık sorunun doğru şıkkıysa doğru (1/1), değilse 0/1.
/// Payload'a bakılmaz ve doğrulanmaz; saklanmaz da (<see cref="Normalize"/> → null, review #571 — MCQ satırına keyfi metin
/// yazılmasın). Geri bildirim yok.
/// </summary>
public sealed class McqGrader : IInteractionGrader
{
    public const string Type = "mcq";

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload) => true;

    public string? Normalize(string? payload) => null;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        var isCorrect = input.SelectedAnswerId != null
            && input.CorrectAnswerId != null
            && input.SelectedAnswerId == input.CorrectAnswerId;
        return InteractionGradeResult.Of(isCorrect ? 1 : 0, 1);
    }

    /// <summary>
    /// issue #584: tek öğe (<c>q</c>), cevap = seçilen şıkkın id'si. Etiket (A, B, …) şık sırasını bilen rapor servisinde konur;
    /// burada id. Doğru şıkkı olmayan soru puanlanamaz → boş.
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (input.CorrectAnswerId == null)
            return [];
        var key = input.SelectedAnswerId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return [new InteractionItemResponse(ItemId, "#1", Grade(input).IsCorrect, key, key)];
    }

    /// <summary>issue #584: MCQ analizinin tek öğesinin id'si.</summary>
    public const string ItemId = "q";
}
