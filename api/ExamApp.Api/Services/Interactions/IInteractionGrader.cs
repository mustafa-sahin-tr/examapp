namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #571: etkileşim tipine göre sunucu tarafı kontrolcü (strateji). Bir tip = bir kontrolcü; seçim
/// <see cref="IInteractionGraderRegistry"/> ile (<c>Question.InteractionType</c>, büyük/küçük harf duyarsız, null = <c>mcq</c>).
/// İki adım: önce <see cref="Validate"/> (geçersiz → 400, hiçbir şey yazılmaz), sonra <see cref="Grade"/>.
/// </summary>
public interface IInteractionGrader
{
    /// <summary>Kontrolcünün tipi (<c>Question.InteractionType</c> değeri, ör. <c>mcq</c>, <c>dragDropLabeling</c>).</summary>
    string InteractionType { get; }

    /// <summary>
    /// Öğrenci payload'u bu plana göre geçerli mi? Boş payload (null) her zaman geçerlidir (cevap temizleme). İstisna atmaz.
    /// </summary>
    bool Validate(string? plan, string? payload);

    /// <summary>
    /// Cevabı kontrol eder. <see cref="Validate"/> geçmiş girdi beklenir; yine de bozuk girdide istisna atmaz (boş cevap gibi
    /// davranır). Çözümü olmayan soru puanlanamaz: <c>(false, 0, 0, null)</c>.
    /// </summary>
    InteractionGradeResult Grade(InteractionGradeInput input);

    /// <summary>
    /// review #571: <see cref="Validate"/>'dan GEÇMİŞ payload'u saklanacak biçime getirir. null = cevap yok (boş satır;
    /// <c>WorksheetScoring</c> boş sayar). Ör. MCQ payload'u saklamaz; <c>dragDropLabeling</c> yerleşimsiz payload'u
    /// (<c>placements: []</c>) null yapar. Varsayılan: aynen.
    /// </summary>
    string? Normalize(string? payload) => payload;
}

/// <summary>review #571: tüm etkileşim tipleri için ortak payload üst sınırı (karakter); save-answer ve kontrol ucu uygular.</summary>
public static class InteractionPayload
{
    public const int MaxLength = 16 * 1024;
}

/// <param name="Plan">Sorunun planı (çözümsüz; eski satırda çözüm içerebilir, kontrolcü yok sayar).</param>
/// <param name="Solution">Etkileşim çözümü (<c>Question.InteractionSolution</c> ya da eski plandan ayrılan); yoksa null.</param>
/// <param name="Payload">Öğrencinin etkileşim cevabı (JSON); yoksa null.</param>
/// <param name="SelectedAnswerId">MCQ: seçilen şık; yoksa null.</param>
/// <param name="CorrectAnswerId">MCQ: sorunun doğru şıkkı; yoksa null.</param>
public sealed record InteractionGradeInput(
    string? Plan,
    string? Solution,
    string? Payload,
    int? SelectedAnswerId,
    int? CorrectAnswerId);

/// <summary>
/// issue #571: kontrol sonucu. <see cref="IsCorrect"/> = <c>CorrectCount == TotalCount &amp;&amp; TotalCount &gt; 0</c>.
/// <see cref="Feedback"/>: satır bazlı JSON (<c>{"rows":[{"id","correct"}]}</c>) ya da null; çözümü İÇERMEZ.
/// </summary>
public sealed record InteractionGradeResult(bool IsCorrect, int CorrectCount, int TotalCount, string? Feedback)
{
    public static InteractionGradeResult Of(int correctCount, int totalCount, string? feedback = null)
        => new(totalCount > 0 && correctCount == totalCount, correctCount, totalCount, feedback);

    /// <summary>Puanlanamaz (ör. çözümü olmayan etkileşimli soru): yanlış, 0/0, geri bildirim yok.</summary>
    public static InteractionGradeResult Ungradable { get; } = new(false, 0, 0, null);
}

/// <summary>issue #571: tip → kontrolcü çözümü.</summary>
public interface IInteractionGraderRegistry
{
    /// <summary>
    /// Tipin kontrolcüsü (büyük/küçük harf duyarsız). null/boş tip = <c>mcq</c>; bilinmeyen tip de bugünkü davranış gereği
    /// MCQ kuralıyla kontrol edilir (seçilen şık == doğru şık).
    /// </summary>
    IInteractionGrader Resolve(string? interactionType);
}
