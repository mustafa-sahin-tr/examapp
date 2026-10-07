using System;
using System.Collections.Generic;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Bir test oturumunun puanlama kuralı — TEK kaynak (issue #421 review M1). Öğrencinin kendi sonuç ekranı
/// (<c>WorksheetDetailService</c>) ve veli test sonucu özeti (<c>ParentAssignmentService</c>) aynı sayıları buradan üretir;
/// eşdeğerlik <c>ParentAssignmentServiceTests.Parent_result_numbers_match_the_student_detail_screen</c>'de aynı oturum iki
/// uçtan puanlanarak doğrulanır.
/// <list type="bullet">
/// <item>Toplam = oturumdaki soru satırı sayısı.</item>
/// <item>Boş = şık seçilmemiş (<c>SelectedAnswerId == null</c>; şıksız/payload cevaplar da boş sayılır — mevcut davranış).</item>
/// <item>Doğru = seçilen şık sorunun doğru şıkkı (doğru şıkkı bilinmeyen/silinmiş soru doğru sayılmaz).</item>
/// <item>Yanlış = kalan.</item>
/// <item>Puan = doğru × 100 / toplam (aşağı yuvarlanır; toplam 0 ise 0).</item>
/// </list>
/// </summary>
public static class WorksheetScoring
{
    public static int ScorePercent(int correct, int total) => total > 0 ? correct * 100 / total : 0;

    /// <summary>Bitiş − başlangıç (saniye, negatif olmaz); bitiş yoksa 0.</summary>
    public static int DurationSeconds(DateTime startTime, DateTime? endTime)
        => endTime.HasValue ? (int)Math.Max(0, (endTime.Value - startTime).TotalSeconds) : 0;

    public static bool IsCorrect(int? selectedAnswerId, int? correctAnswerId)
        => selectedAnswerId != null && correctAnswerId != null && correctAnswerId == selectedAnswerId;

    /// <param name="correctAnswerOf">Satırın sorusunun doğru şıkkı; soru bilinmiyorsa null.</param>
    public static (int Correct, int Wrong, int Blank) Tally<T>(
        IEnumerable<T> answers, Func<T, int?> selectedAnswerOf, Func<T, int?> correctAnswerOf)
    {
        int correct = 0, wrong = 0, blank = 0;
        foreach (var answer in answers)
        {
            var selected = selectedAnswerOf(answer);
            if (selected == null)
                blank++;
            else if (IsCorrect(selected, correctAnswerOf(answer)))
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
