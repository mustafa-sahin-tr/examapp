using System;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Api.Services.Practice;

/// <summary>Bir sorunun tek cevabı: yanlış ve boş cevap <c>Correct = false</c> sayılır.</summary>
/// <param name="SelectedAnswerId">Issue #862: öğrencinin seçtiği şık (çoktan seçmeli değilse ya da boşsa null).</param>
internal readonly record struct DueReviewAttempt(DateTime AtUtc, bool Correct, int? SelectedAnswerId = null);

/// <summary>Tekrar sırası: <c>Step</c> son yanlıştan sonraki doğru sayısı, <c>DueDay</c> tekrarın geldiği yerel gün.</summary>
internal readonly record struct DueReviewStatus(int Step, DateOnly DueDay, int WrongCount);

/// <summary>
/// Aralıklı tekrar kuralı (issue #529). Son yanlıştan sonra doğru çözüldükçe aralık büyür (1, 3, 7, 21 gün);
/// aralık son cevaptan başlar. Bütün basamaklar doğru geçilince soru listeden çıkar. Saf: test edilebilir.
/// </summary>
internal static class DueReviewSchedule
{
    internal static readonly int[] IntervalDays = [1, 3, 7, 21];

    /// <returns>null: soru hiç yanlış yapılmadı ya da bütün basamaklar geçildi.</returns>
    internal static DueReviewStatus? Evaluate(IEnumerable<DueReviewAttempt> attempts, TimeZoneInfo timeZone)
    {
        var ordered = attempts.OrderBy(a => a.AtUtc).ToList();
        var lastWrong = ordered.FindLastIndex(a => !a.Correct);
        if (lastWrong < 0)
            return null;

        var step = ordered.Count - lastWrong - 1;
        if (step >= IntervalDays.Length)
            return null;

        var last = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ordered[^1].AtUtc, DateTimeKind.Utc), timeZone);
        var dueDay = DateOnly.FromDateTime(last).AddDays(IntervalDays[step]);
        return new DueReviewStatus(step, dueDay, ordered.Count(a => !a.Correct));
    }
}
