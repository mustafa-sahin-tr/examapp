using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos;

/// <summary>POST api/practice/sessions gövdesi. İkisi de boşsa: öğrencinin sınıfı + tüm dersler.</summary>
public class PracticeSessionStartDto
{
    /// <summary>Issue #524: Ui (default) | Assistant | DeepLink | Notification; another value is 400.</summary>
    [MaxLength(20)]
    public string? StartedVia { get; set; }

    [MaxLength(50)]
    public List<int>? SubjectIds { get; set; }

    [MaxLength(50)]
    public List<int>? TopicIds { get; set; }

    /// <summary>Issue #500: öğrencinin sınıfından olmalı; başka sınıfın alt konusu 400.</summary>
    [MaxLength(50)]
    public List<int>? SubTopicIds { get; set; }

    /// <summary>Issue #500: "10 soru getir". Boşsa havuz bitene kadar sürer.</summary>
    [Range(1, 50)]
    public int? TargetCount { get; set; }

    /// <summary>Issue #529: oturum, tekrar zamanı gelen sorulardan sabit bir listeyle açılır.</summary>
    public bool DueReview { get; set; }

    /// <summary>Issue #698: oturum, yanlış defterinden (kapsam filtresiyle) sabit bir listeyle açılır.</summary>
    public bool Mistakes { get; set; }

    /// <summary>Issue #862: <see cref="Mistakes"/> ile tek soru ("Tekrar çöz"); soru defterde değilse oturum açılmaz.</summary>
    public int? MistakeQuestionId { get; set; }

    /// <summary>Issue #532: oturum, öğrencinin kaydettiği sorulardan sabit bir listeyle açılır.</summary>
    public bool Saved { get; set; }

    /// <summary>Issue #534: oturum, tekrar, yanlış defteri ve zayıf konulardan karışık sabit bir listeyle açılır.</summary>
    public bool MixedReview { get; set; }

    /// <summary>Issue #933: oturum, yayımlanmış çalışma sayfasının soru bloklarından sabit bir listeyle açılır.</summary>
    public int? StudyItemId { get; set; }

    /// <summary>Issue #933: <see cref="StudyItemId"/> ile tek soru; soru sayfada değilse oturum açılmaz.</summary>
    public int? StudyQuestionId { get; set; }

    /// <summary>
    /// Issue #526: oturum bu sorularla, bu sırayla açılır (zorlandığım sorular). Her id öğrencinin zor soru listesinde olmalı
    /// (<c>GET api/student/me/hard-questions</c> kuralı); olmayan id 400, sessizce atlanmaz.
    /// </summary>
    [MaxLength(30)]
    public List<int>? QuestionIds { get; set; }
}

/// <summary>
/// Issue #526: one of the student's hard questions. No correct choice (the assistant explains through #510). Attempts, misses and
/// the last attempt are the student's own (zero / null in <c>global</c> mode when they never answered it); <c>CorrectPercent</c>
/// is the share of students who answered it correctly (<c>global</c> mode only, an aggregate: no other student's data).
/// </summary>
public sealed record HardQuestionDto(
    int QuestionId,
    int? SubjectId,
    string? SubjectName,
    int? TopicId,
    string? TopicName,
    List<string> SubTopicNames,
    int Attempts,
    int MissCount,
    bool MissedAfterCorrect,
    bool LastAttemptCorrect,
    DateTime? LastAttemptAt,
    [property: StorageUrl(StorageArea.QuestionImage)] string? ImageUrl,
    string? Text,
    int? CorrectPercent = null);

/// <summary>GET api/student/me/hard-questions.</summary>
public sealed record HardQuestionsDto(string Mode, int? Days, int TotalCount, List<HardQuestionDto> Items);

/// <summary>
/// Issue #527: a question the student answered much slower (<c>slow</c>, right or wrong) or wrong much faster (<c>rushed</c>)
/// than its average time. <c>AverageScope</c> is <c>question</c> or <c>subTopic</c> (too few answers to the question itself).
/// Times in seconds; the average is an aggregate of the other students' timed answers. No correct choice.
/// </summary>
public sealed record TimeStruggleDto(
    int QuestionId,
    int? SubjectId,
    string? SubjectName,
    int? TopicId,
    string? TopicName,
    List<string> SubTopicNames,
    int TimeTakenSeconds,
    int AverageSeconds,
    string AverageScope,
    bool Correct,
    DateTime AnsweredAt,
    [property: StorageUrl(StorageArea.QuestionImage)] string? ImageUrl,
    string? Text);

/// <summary>Issue #527: <c>GET api/student/me/time-struggles</c>.</summary>
public sealed record TimeStrugglesDto(string Kind, int Days, int TotalCount, List<TimeStruggleDto> Items);

/// <summary>
/// Issue #533: the student's accuracy in a topic next to their grade's (an aggregate of at least <c>HardQuestions:MinStudents</c>
/// students, else null). No other student's data, no rank or percentile.
/// </summary>
public sealed record TopicPeerComparisonDto(
    int? SubjectId,
    string? SubjectName,
    int TopicId,
    string? TopicName,
    int MyAnswered,
    int MyAccuracyPct,
    int? PeerAccuracyPct,
    int? PeerMedianPct);

/// <summary>Issue #533: <c>GET api/student/me/topic-comparison</c>; <c>ComputedAtUtc</c> is when the peer stats were computed.</summary>
public sealed record TopicPeerComparisonsDto(int Days, int MinAnswers, DateTime? ComputedAtUtc, List<TopicPeerComparisonDto> Items);

/// <summary>Issue #528: one example of a repeated trap: the student's own choice (label A, B, … and text), never the correct one.</summary>
public sealed record RepeatedTrapExampleDto(
    int QuestionId,
    string? ChosenLabel,
    string? ChosenText,
    DateTime AnsweredAt,
    [property: StorageUrl(StorageArea.QuestionImage)] string? ImageUrl,
    string? Text);

/// <summary>Issue #528: a subtopic (or a topic, for questions without subtopics) where the student keeps picking the popular distractor.</summary>
public sealed record RepeatedTrapDto(
    int? SubjectId,
    string? SubjectName,
    int TopicId,
    string? TopicName,
    int? SubTopicId,
    string? SubTopicName,
    int MistakeCount,
    int TrapCount,
    int StrengthPct,
    List<RepeatedTrapExampleDto> Examples);

/// <summary>Issue #528: <c>GET api/student/me/repeated-traps</c>.</summary>
public sealed record RepeatedTrapsDto(int Days, int MinMistakes, int TotalCount, List<RepeatedTrapDto> Items);

/// <summary>GET api/practice/saved: kaydedilen sorular, en yeni önce.</summary>
public class SavedQuestionsDto
{
    public int TotalCount { get; set; }
    public List<SavedQuestionDto> Items { get; set; } = new();
}

public class SavedQuestionDto
{
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? TopicName { get; set; }
    public DateTime SavedAt { get; set; }
}

/// <summary>GET api/practice/mistakes: aktif yanlışlar, en yeni yanlış önce.</summary>
public class MistakeNotebookDto
{
    /// <summary>Aktif toplam; <see cref="Items"/> bunun ilk bölümüdür.</summary>
    public int TotalCount { get; set; }
    public List<MistakeItemDto> Items { get; set; } = new();
}

public class MistakeItemDto
{
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public DateTime LastWrongAt { get; set; }
    public int WrongCount { get; set; }
    /// <summary>DidNotKnow | Calculation | Attention | Time; etiket yoksa null.</summary>
    public string? Reason { get; set; }
    /// <summary>Issue #862: sorunun görseli (yoksa null; görselsiz soruda <see cref="Text"/>). Yanıtta imzalanır (#365).</summary>
    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
    public string? Text { get; set; }
    /// <summary>Issue #862: son yanlışta seçilen şıkkın harfi (boş bırakıldıysa / şıksız soruda null).</summary>
    public string? YourAnswer { get; set; }
    /// <summary>Issue #862: doğru şıkkın harfi (şıksız soruda null).</summary>
    public string? CorrectAnswer { get; set; }
    /// <summary>Issue #862: son yanlıştan sonraki doğru sayısı; <see cref="CorrectToClear"/>'a varınca defterden çıkar.</summary>
    public int CorrectSinceWrong { get; set; }
    public int CorrectToClear { get; set; }
}

public class MistakeReasonUpdateDto
{
    /// <summary>DidNotKnow | Calculation | Attention | Time; null etiketi kaldırır.</summary>
    public string? Reason { get; set; }
}

/// <summary>GET api/practice/due-review: tekrar zamanı gelen sorular, en çok geciken önce.</summary>
public class DueReviewDto
{
    /// <summary>Tekrar zamanı gelen toplam soru; <see cref="Items"/> bunun ilk bölümüdür.</summary>
    public int TotalCount { get; set; }
    public List<DueReviewItemDto> Items { get; set; } = new();
}

public class DueReviewItemDto
{
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? TopicName { get; set; }
    public DateTime DueSince { get; set; }
    public int Step { get; set; }
    public int WrongCount { get; set; }
}

public class PracticeSessionDto
{
    public int Id { get; set; }
    public int GradeId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Active | Ended</summary>
    public string Status { get; set; } = string.Empty;

    public List<int> SubjectIds { get; set; } = new();
    public List<int> TopicIds { get; set; } = new();
    public List<int> SubTopicIds { get; set; } = new();
    public int? TargetCount { get; set; }

    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int SkippedCount { get; set; }

    /// <summary>Issue #529: tekrar oturumu; soruları sabit listeden gelir.</summary>
    public bool IsDueReview { get; set; }
}

/// <summary>
/// GET api/practice/sessions/{id}/next cevabı. Havuz bittiğinde 404 değil,
/// <c>Question = null</c> + <c>PoolExhausted = true</c> ile 200 döner.
/// </summary>
public class PracticeNextQuestionDto
{
    public int SessionId { get; set; }

    public QuestionDto? Question { get; set; }

    public bool PoolExhausted { get; set; }

    /// <summary>Issue #500: oturum istenen soru sayısına ulaştı; havuz bitmiş değil.</summary>
    public bool TargetReached { get; set; }

    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
}

/// <summary>POST api/practice/sessions/{id}/answer gövdesi.</summary>
public class PracticeAnswerSubmitDto
{
    [Required]
    public int QuestionId { get; set; }

    /// <summary>null = pas (Skipped=true olmalı) ya da şıksız etkileşim.</summary>
    public int? SelectedAnswerId { get; set; }

    public bool Skipped { get; set; }

    /// <summary>Saniye. Üst sınır 1 saat (issue #99 security D2: günlük set cevabı süreyi puan/aktivite hattına taşır).</summary>
    [Range(0, MaxTimeTakenSeconds)]
    public int TimeTaken { get; set; }

    public const int MaxTimeTakenSeconds = 3600;
}

/// <summary>
/// GET api/practice/sessions/{id}/review cevabı. Oturum özeti + gösterilen soruların tek tek sonucu.
/// Her satırda canvas çizimi için tam <see cref="QuestionDto"/> (geometri, şıklar, passage) bulunur;
/// worksheet sonuç ekranındaki <c>WorksheetInstanceQuestionDto</c> ile aynı yaklaşım.
/// Cevaplanmış (Answered/Skipped) sorularda <see cref="QuestionDto.CorrectAnswerId"/> ve
/// <see cref="AnswerDto.IsCorrect"/> dolu gelir; Pending sorularda gizlenir.
/// </summary>
public class PracticeSessionReviewDto
{
    public PracticeSessionDto Session { get; set; } = new();

    /// <summary><see cref="PracticeSessionReviewQuestionDto.ShownAt"/> sırasıyla.</summary>
    public List<PracticeSessionReviewQuestionDto> Questions { get; set; } = new();
}

public class PracticeSessionReviewQuestionDto
{
    /// <summary>
    /// Canlı akıştaki (<see cref="PracticeNextQuestionDto.Question"/>) ile aynı tam şekil.
    /// <c>CorrectAnswerId</c> / <c>Answers[].IsCorrect</c> yalnızca <see cref="AnsweredAt"/> doluysa açıklanır.
    /// </summary>
    public QuestionDto Question { get; set; } = new();

    /// <summary>Pending (gösterildi, cevaplanmadı) | Answered | Skipped</summary>
    public string Status { get; set; } = string.Empty;

    public bool IsSkipped { get; set; }
    public bool IsCorrect { get; set; }
    public int? SelectedAnswerId { get; set; }

    /// <summary>Saniye.</summary>
    public int TimeTaken { get; set; }

    public DateTime ShownAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

public class PracticeAnswerResultDto
{
    public int SessionId { get; set; }
    public int QuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public bool Skipped { get; set; }

    /// <summary>Pratikte anında geri bildirim için doğru şık.</summary>
    public int? CorrectAnswerId { get; set; }

    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
}

/// <summary>Issue #524: number of sessions with one start source and kind (<see cref="Daily"/>: the daily set's session).</summary>
public sealed record PracticeStartCountDto(ExamApp.Api.Data.PracticeStartedVia StartedVia, bool Daily, int Count);
