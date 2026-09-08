using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

/// <summary>POST api/practice/sessions gövdesi. İkisi de boşsa: öğrencinin sınıfı + tüm dersler.</summary>
public class PracticeSessionStartDto
{
    [MaxLength(50)]
    public List<int>? SubjectIds { get; set; }

    [MaxLength(50)]
    public List<int>? TopicIds { get; set; }
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

    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int SkippedCount { get; set; }
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

    /// <summary>Saniye.</summary>
    [Range(0, int.MaxValue)]
    public int TimeTaken { get; set; }
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
