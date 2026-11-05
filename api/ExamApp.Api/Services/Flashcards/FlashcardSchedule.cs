using System;
using ExamApp.Api.Services.Practice;

namespace ExamApp.Api.Services.Flashcards;

public enum FlashcardRecall
{
    Forgot = 0,
    Hard = 1,
    Good = 2,
}

/// <summary>
/// Issue #699: Leitner kutuları, aralıklar <see cref="DueReviewSchedule.IntervalDays"/> (1, 3, 7, 21 gün). Unuttu: ilk kutu,
/// zor: aynı kutu, iyi: bir üst kutu. Son kutudaki kart listeden çıkmaz, aralığıyla gelmeye devam eder. Saf: test edilebilir.
/// </summary>
internal static class FlashcardSchedule
{
    internal static (int Box, DateOnly DueDay) Next(int? box, FlashcardRecall recall, DateOnly today)
    {
        var current = box ?? 0;
        var next = recall switch
        {
            FlashcardRecall.Forgot => 0,
            FlashcardRecall.Hard => current,
            FlashcardRecall.Good => Math.Min(current + 1, DueReviewSchedule.IntervalDays.Length - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(recall)),
        };
        return (next, today.AddDays(DueReviewSchedule.IntervalDays[next]));
    }
}
