using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Pratik oturumu (issue #62). Havuz kuralı, <see cref="Helpers.WorksheetAccess.CanStudentStartTest"/>'in
/// atama-dışı yarısıyla tutarlıdır: soru, sınıfı öğrencininkiyle eşleşen ve
/// <see cref="WorksheetStudentVisibility.Normal"/> olan en az bir worksheet'te bulunmalı; ayrıca
/// <c>TopicId</c> doluysa <c>Topic.GradeId</c> de eşleşmeli.
/// Outbox event üretmez; puan/rozet akışına dokunmaz.
/// </summary>
public class PracticeSessionService : IPracticeSessionService
{
    private readonly AppDbContext _context;

    public PracticeSessionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PracticeSessionDto> StartAsync(StudentProfileDto student, PracticeSessionStartDto request, CancellationToken ct = default)
    {
        if (!student.GradeId.HasValue)
            throw new InvalidOperationException("Pratik yapabilmek için sınıf bilginizin tanımlı olması gerekir");

        var subjectIds = Normalize(request.SubjectIds);
        var topicIds = Normalize(request.TopicIds);

        var session = new PracticeSession
        {
            StudentId = student.Id,
            GradeId = student.GradeId.Value,
            StartTime = DateTime.UtcNow,
            Status = PracticeSessionStatus.Active,
            SubjectIdsJson = subjectIds.Count > 0 ? JsonSerializer.Serialize(subjectIds) : null,
            TopicIdsJson = topicIds.Count > 0 ? JsonSerializer.Serialize(topicIds) : null
        };

        _context.PracticeSessions.Add(session);
        await _context.SaveChangesAsync(ct);

        return MapToDto(session);
    }

    public async Task<PracticeSessionDto?> GetAsync(int sessionId, int studentId, CancellationToken ct = default)
    {
        var session = await _context.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == studentId, ct);

        return session == null ? null : MapToDto(session);
    }

    public async Task<PracticeNextQuestionDto?> NextQuestionAsync(int sessionId, int studentId, CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, studentId, ct);
        if (session == null)
            return null;

        EnsureActive(session);

        // Gösterilmiş ama cevaplanmamış soru varsa (örn. sayfa yenilendi) yenisini seçme, onu döndür.
        var pending = session.Questions.FirstOrDefault(q => q.AnsweredAt == null);
        int questionId;

        if (pending != null)
        {
            questionId = pending.QuestionId;
        }
        else
        {
            var shownIds = session.Questions.Select(q => q.QuestionId).ToList();

            var pool = BuildPool(session.GradeId, ParseIds(session.SubjectIdsJson), ParseIds(session.TopicIdsJson))
                .Where(q => !shownIds.Contains(q.Id));

            var count = await pool.CountAsync(ct);
            if (count == 0)
            {
                return new PracticeNextQuestionDto
                {
                    SessionId = session.Id,
                    Question = null,
                    PoolExhausted = true,
                    AnsweredCount = session.Questions.Count(q => q.AnsweredAt != null),
                    CorrectCount = session.Questions.Count(q => q.IsCorrect)
                };
            }

            // Rastgele seçim: sayım + rastgele offset. MVP ölçeğinde yeterli; tek satır çeker.
            var skip = Random.Shared.Next(count);
            questionId = await pool
                .OrderBy(q => q.Id)
                .Skip(skip)
                .Select(q => q.Id)
                .FirstAsync(ct);

            session.Questions.Add(new PracticeSessionQuestion
            {
                QuestionId = questionId,
                ShownAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }

        var question = await _context.Questions
            .AsNoTracking()
            .Include(q => q.Answers)
            .Include(q => q.Passage)
            .FirstAsync(q => q.Id == questionId, ct);

        return new PracticeNextQuestionDto
        {
            SessionId = session.Id,
            Question = MapQuestion(question),
            PoolExhausted = false,
            AnsweredCount = session.Questions.Count(q => q.AnsweredAt != null),
            CorrectCount = session.Questions.Count(q => q.IsCorrect)
        };
    }

    public async Task<PracticeAnswerResultDto?> SubmitAnswerAsync(int sessionId, int studentId, PracticeAnswerSubmitDto dto, CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, studentId, ct);
        if (session == null)
            return null;

        EnsureActive(session);

        var psq = session.Questions.FirstOrDefault(q => q.QuestionId == dto.QuestionId);
        if (psq == null)
            throw new InvalidOperationException("Bu soru bu oturumda gösterilmedi");

        if (psq.AnsweredAt != null)
            throw new InvalidOperationException("Bu soru zaten cevaplandı");

        var question = await _context.Questions
            .AsNoTracking()
            .Where(q => q.Id == dto.QuestionId)
            .Select(q => new { q.CorrectAnswerId, AnswerIds = q.Answers.Select(a => a.Id).ToList() })
            .FirstOrDefaultAsync(ct);

        if (question == null)
            throw new InvalidOperationException("Soru bulunamadı");

        var skipped = dto.Skipped;
        int? selectedAnswerId = null;
        var isCorrect = false;

        if (!skipped)
        {
            if (!dto.SelectedAnswerId.HasValue)
                throw new InvalidOperationException("Bir şık seçilmeli ya da soru pas geçilmeli");

            if (!question.AnswerIds.Contains(dto.SelectedAnswerId.Value))
                throw new InvalidOperationException("Seçilen şık bu soruya ait değil");

            selectedAnswerId = dto.SelectedAnswerId;
            isCorrect = question.CorrectAnswerId.HasValue && question.CorrectAnswerId.Value == selectedAnswerId.Value;
        }

        psq.SelectedAnswerId = selectedAnswerId;
        psq.IsCorrect = isCorrect;
        psq.IsSkipped = skipped;
        psq.TimeTaken = Math.Max(0, dto.TimeTaken);
        psq.AnsweredAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new PracticeAnswerResultDto
        {
            SessionId = session.Id,
            QuestionId = dto.QuestionId,
            IsCorrect = isCorrect,
            Skipped = skipped,
            CorrectAnswerId = question.CorrectAnswerId,
            AnsweredCount = session.Questions.Count(q => q.AnsweredAt != null),
            CorrectCount = session.Questions.Count(q => q.IsCorrect)
        };
    }

    public async Task<PracticeSessionDto?> EndAsync(int sessionId, int studentId, CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, studentId, ct);
        if (session == null)
            return null;

        if (session.Status == PracticeSessionStatus.Active)
        {
            session.Status = PracticeSessionStatus.Ended;
            session.EndTime = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return MapToDto(session);
    }

    // --- havuz ---

    private IQueryable<Question> BuildPool(int gradeId, List<int> subjectIds, List<int> topicIds)
    {
        var pool = _context.Questions
            .AsNoTracking()
            // Havuz filtresi: sınıfı eşleşen ve Normal görünürlükte en az bir worksheet'te olmalı.
            .Where(q => q.WorksheetQuestions.Any(wq =>
                wq.Worksheet.GradeId == gradeId &&
                wq.Worksheet.StudentVisibility == WorksheetStudentVisibility.Normal))
            // Sınıf eşleşmesi: TopicId doluysa Topic.GradeId belirleyici; boşsa worksheet sınıfı (yukarıda) yeter.
            .Where(q => q.TopicId == null || q.Topic.GradeId == gradeId)
            // Pratik değerlendirmesi SelectedAnswerId <-> CorrectAnswerId karşılaştırmasına dayanır;
            // sürükle-bırak gibi CorrectAnswerId'siz etkileşim tipleri bu MVP'de değerlendirilemez.
            .Where(q => q.CorrectAnswerId != null);

        if (subjectIds.Count > 0)
            pool = pool.Where(q => q.SubjectId != null && subjectIds.Contains(q.SubjectId.Value));

        if (topicIds.Count > 0)
            pool = pool.Where(q => q.TopicId != null && topicIds.Contains(q.TopicId.Value));

        return pool;
    }

    // --- helpers ---

    private Task<PracticeSession?> LoadOwnedSessionAsync(int sessionId, int studentId, CancellationToken ct) =>
        _context.PracticeSessions
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == studentId, ct);

    private static void EnsureActive(PracticeSession session)
    {
        if (session.Status != PracticeSessionStatus.Active)
            throw new InvalidOperationException("Bu pratik oturumu sonlandırılmış");
    }

    private static List<int> Normalize(IEnumerable<int>? ids) =>
        ids?.Where(id => id > 0).Distinct().OrderBy(id => id).ToList() ?? new List<int>();

    private static List<int> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<int>();

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
        }
        catch (JsonException)
        {
            return new List<int>();
        }
    }

    private static PracticeSessionDto MapToDto(PracticeSession s) => new()
    {
        Id = s.Id,
        GradeId = s.GradeId,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        Status = s.Status.ToString(),
        SubjectIds = ParseIds(s.SubjectIdsJson),
        TopicIds = ParseIds(s.TopicIdsJson),
        AnsweredCount = s.Questions.Count(q => q.AnsweredAt != null),
        CorrectCount = s.Questions.Count(q => q.IsCorrect),
        SkippedCount = s.Questions.Count(q => q.IsSkipped)
    };

    /// <summary>Doğru cevap bilinçli olarak dahil edilmez; cevap sonrası <see cref="PracticeAnswerResultDto"/> ile döner.</summary>
    private static QuestionDto MapQuestion(Question q) => new()
    {
        Id = q.Id,
        Text = q.Text ?? string.Empty,
        SubText = q.SubText,
        ImageUrl = q.ImageUrl,
        SubjectId = q.SubjectId,
        TopicId = q.TopicId,
        Point = q.Point,
        DifficultyLevel = q.DifficultyLevel,
        IsExample = q.IsExample,
        InteractionType = q.InteractionType,
        InteractionPlan = q.InteractionPlan,
        ShowPassageFirst = q.ShowPassageFirst,
        PracticeCorrectAnswer = q.PracticeCorrectAnswer,
        AnswerColCount = q.AnswerColCount,
        IsCanvasQuestion = q.IsCanvasQuestion,
        X = q.X,
        Y = q.Y,
        Width = q.Width,
        Height = q.Height,
        SanitizedHeight = q.SanitizedHeight,
        Passage = q.PassageId.HasValue && q.Passage != null
            ? new PassageDto
            {
                Id = q.Passage.Id,
                Title = q.Passage.Title,
                Text = q.Passage.Text,
                ImageUrl = q.Passage.ImageUrl,
                X = q.Passage.X,
                Y = q.Passage.Y,
                Width = q.Passage.Width,
                Height = q.Passage.Height
            }
            : null,
        Answers = q.Answers
            .OrderBy(a => a.Order ?? int.MaxValue)
            .ThenBy(a => a.Id)
            .Select(a => new AnswerDto
            {
                Id = a.Id,
                Text = a.Text,
                ImageUrl = a.ImageUrl,
                X = a.X,
                Y = a.Y,
                Width = a.Width,
                Height = a.Height,
                Tag = a.Tag,
                Order = a.Order
            })
            .ToList()
    };
}
