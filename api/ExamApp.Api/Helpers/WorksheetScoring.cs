using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Bir test oturumunun puanlama kuralı — TEK kaynak (issue #421 review M1). Öğrencinin kendi sonuç ekranı
/// (<c>WorksheetDetailService</c>), veli test sonucu özeti (<c>ParentAssignmentService</c>), tamamlanan testler listesi ve veli
/// bildirimi (<c>TestSessionService</c>), test listesi (<c>ExamService</c>) ve "yanlışlarım" listeleri aynı sayıları buradan
/// üretir; eşdeğerlik <c>ParentAssignmentServiceTests.Parent_result_numbers_match_the_student_detail_screen</c>'de aynı oturum
/// iki uçtan puanlanarak doğrulanır.
/// <para>
/// issue #571 — hibrit kural (MCQ sonuçları değişmez; etkileşimli cevaplar da sayılır):
/// </para>
/// <list type="bullet">
/// <item>Toplam = oturumdaki soru satırı sayısı — issue #583 (savunma): açık uçlu (<c>openText</c>) satırlar hariç; bu satırlar
/// ne doğru ne yanlış ne boş sayılır (öğretmen değerlendirmesi puansızdır). openText teste giremez (kayıt yolları reddeder);
/// kural yalnız eski/kaçak veride skoru bozmamak için.</item>
/// <item>Doğru = (şık seçilmiş VE seçilen şık sorunun doğru şıkkı) YA DA (şık seçilmemiş, etkileşim cevabı
/// (<c>AnswerPayload</c>) var VE kontrolcü tamamını doğru bulmuş — kayıtlı <c>IsCorrect</c>). Doğru şıkkı bilinmeyen/silinmiş
/// soru şık cevabında doğru sayılmaz.</item>
/// <item>Boş = şık seçilmemiş VE etkileşim cevabı yok.</item>
/// <item>Yanlış = kalan. Sorusu silinmiş/bilinmeyen (soft-delete) satır boş değilse HER ZAMAN yanlıştır — şık da etkileşim
/// cevabı da (kayıtlı <c>IsCorrect</c>'e bakılmaz).</item>
/// <item>Puan = doğru × 100 / toplam (aşağı yuvarlanır; toplam 0 ise 0). Kısmi puan yok (satır bazlı
/// <c>CorrectCount</c>/<c>TotalCount</c> yalnız geri bildirimdir).</item>
/// </list>
/// Sorgu tarafı <see cref="IsCorrectRow"/> / <see cref="IsWrongRow"/> / <see cref="IsBlankRow"/> (EF'e çevrilebilir;
/// gezinme koleksiyonunda <c>.AsQueryable().Count(...)</c> ile), bellek tarafı <see cref="Tally{T}"/>. <c>Worksheet.Kind ==
/// Activity</c> (etkinlik) oturumları skor/not hesaplarına hiç girmez — çağıranlar filtreler.
/// <para>
/// Silinmiş soru ve SQL (review #571): <see cref="IsCorrectRow"/>/<see cref="IsWrongRow"/> sorunun doğru şıkkına
/// <c>WorksheetQuestion.Question</c> gezinmesiyle bakar; soft-delete query filter'lı zorunlu gezinme INNER JOIN olduğundan
/// sorusu silinmiş satır bu iki sayımda HİÇ görünmez (ne doğru ne yanlış). <see cref="IsBlankRow"/> gezinmesizdir. Bu yüzden
/// sayım yapan sorgular yanlışı <c>toplam − doğru − boş</c> olarak hesaplar (bellek kuralıyla aynı: silinmiş sorunun boş olmayan
/// satırı yanlış); <see cref="IsWrongRow"/> yalnız "yanlış soruları listele" gibi sorusu var olan satırlara bakan yerlerde
/// kullanılır.
/// </para>
/// </summary>
public static class WorksheetScoring
{
    public static int ScorePercent(int correct, int total) => total > 0 ? correct * 100 / total : 0;

    /// <summary>Bitiş − başlangıç (saniye, negatif olmaz); bitiş yoksa 0.</summary>
    public static int DurationSeconds(DateTime startTime, DateTime? endTime)
        => endTime.HasValue ? (int)Math.Max(0, (endTime.Value - startTime).TotalSeconds) : 0;

    /// <summary>issue #571: satır doğru mu (sınıf dokümanındaki hibrit kural; <see cref="IsCorrectRow"/> ile aynı).</summary>
    /// <param name="hasPayload">Satırda etkileşim cevabı (<c>AnswerPayload != null</c>) var mı.</param>
    /// <param name="storedIsCorrect">Satırın kayıtlı <c>IsCorrect</c>'i (kontrolcü sonucu).</param>
    /// <param name="questionKnown">Satırın sorusu biliniyor mu (silinmiş/bilinmeyen soru → hiçbir cevap doğru değil).</param>
    public static bool IsCorrect(int? selectedAnswerId, int? correctAnswerId, bool hasPayload, bool storedIsCorrect,
        bool questionKnown = true)
        => questionKnown
            && (selectedAnswerId != null
                ? correctAnswerId != null && correctAnswerId == selectedAnswerId
                : hasPayload && storedIsCorrect);

    /// <summary>issue #571: boş = şık yok VE etkileşim cevabı yok.</summary>
    public static bool IsBlank(int? selectedAnswerId, bool hasPayload) => selectedAnswerId == null && !hasPayload;

    /// <summary>issue #571: sorgu tarafı "doğru" — <see cref="IsCorrect(int?, int?, bool, bool)"/> ile aynı kural.</summary>
    public static readonly Expression<Func<WorksheetInstanceQuestion, bool>> IsCorrectRow = wiq =>
        (wiq.SelectedAnswerId != null && wiq.SelectedAnswerId == wiq.WorksheetQuestion.Question.CorrectAnswerId)
        || (wiq.SelectedAnswerId == null && wiq.AnswerPayload != null && wiq.IsCorrect);

    /// <summary>
    /// issue #571: sorgu tarafı "yanlış" = boş değil ve doğru değil — yalnız sorusu VAR olan satırlar için (bkz. sınıf dokümanı:
    /// sayımda <c>toplam − doğru − boş</c> kullanın).
    /// </summary>
    public static readonly Expression<Func<WorksheetInstanceQuestion, bool>> IsWrongRow = wiq =>
        ((wiq.SelectedAnswerId != null && wiq.SelectedAnswerId != wiq.WorksheetQuestion.Question.CorrectAnswerId)
            || (wiq.SelectedAnswerId == null && wiq.AnswerPayload != null && !wiq.IsCorrect))
        // issue #583: açık uçlu satır yanlış sayılmaz (puanlamaya girmez).
        && wiq.WorksheetQuestion.Question.InteractionType != ExamApp.Api.Services.Interactions.OpenTextGrader.Type;

    /// <summary>issue #571: sorgu tarafı "boş".</summary>
    public static readonly Expression<Func<WorksheetInstanceQuestion, bool>> IsBlankRow = wiq =>
        wiq.SelectedAnswerId == null && wiq.AnswerPayload == null;

    /// <summary>issue #571: satır başına puanlamaya giren alanlar (bellek tarafı).</summary>
    /// <param name="CorrectAnswerId">Satırın sorusunun doğru şıkkı; soru bilinmiyorsa null.</param>
    /// <param name="QuestionKnown">Satırın sorusu biliniyor mu (silinmiş soru → boş değilse yanlış; SQL sayımıyla aynı).</param>
    /// <param name="Excluded">issue #583: puanlamaya girmeyen satır (açık uçlu); hiçbir sayıma ve toplama girmez.</param>
    public readonly record struct AnswerFacts(
        int? SelectedAnswerId, bool HasPayload, bool StoredIsCorrect, int? CorrectAnswerId, bool QuestionKnown = true,
        bool Excluded = false);

    /// <summary>issue #583: puanlamaya giren satır sayısı (toplam) — <see cref="AnswerFacts.Excluded"/> satırlar hariç.</summary>
    public static int Total<T>(IEnumerable<T> answers, Func<T, AnswerFacts> factsOf)
    {
        var total = 0;
        foreach (var answer in answers)
        {
            if (!factsOf(answer).Excluded)
                total++;
        }
        return total;
    }

    /// <summary>
    /// issue #583: sorgu tarafı "puanlanan satır" — sorusu açık uçlu (<c>openText</c>) olmayan satır. Bağlam üzerinden korelasyonlu
    /// EXISTS ile (gezinme JOIN'i değil): sorusu / bağı silinmiş satır eskisi gibi sayılır. Toplam ve boş sayımlarında kullanılır.
    /// </summary>
    public static Expression<Func<WorksheetInstanceQuestion, bool>> IsScoredRow(ExamApp.Api.Data.AppDbContext db) => wiq =>
        !db.TestQuestions.Any(wq => wq.Id == wiq.WorksheetQuestionId
            && wq.Question.InteractionType == ExamApp.Api.Services.Interactions.OpenTextGrader.Type);

    public static (int Correct, int Wrong, int Blank) Tally<T>(IEnumerable<T> answers, Func<T, AnswerFacts> factsOf)
    {
        int correct = 0, wrong = 0, blank = 0;
        foreach (var answer in answers)
        {
            var f = factsOf(answer);
            if (f.Excluded)
                continue;
            if (IsBlank(f.SelectedAnswerId, f.HasPayload))
                blank++;
            else if (IsCorrect(f.SelectedAnswerId, f.CorrectAnswerId, f.HasPayload, f.StoredIsCorrect, f.QuestionKnown))
                correct++;
            else
                wrong++;
        }

        return (correct, wrong, blank);
    }

    /// <summary>
    /// Konu kırılımının grup anahtarı. Soru bilinmiyorsa (<paramref name="questionKnown"/> false) <c>(null, unclassified)</c>;
    /// biliniyorsa sorunun konu id'si ve adı, adı yoksa (konusuz ya da konu silinmiş) <paramref name="unclassifiedName"/>.
    /// </summary>
    public static (int? TopicId, string Name) TopicKey(bool questionKnown, int? topicId, string? topicName, string unclassifiedName)
        => questionKnown ? (topicId, topicName ?? unclassifiedName) : (null, unclassifiedName);
}
