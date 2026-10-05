using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Pratik oturumu (issue #62). Havuz kuralı, <see cref="Helpers.WorksheetAccess.CanStudentStartTest"/>'in
/// atama-dışı yarısıyla tutarlıdır: soru, sınıfı öğrencininkiyle eşleşen ve
/// <see cref="WorksheetStudentVisibility.Normal"/> olan en az bir worksheet'te bulunmalı; ayrıca
/// <c>TopicId</c> doluysa <c>Topic.GradeId</c> de eşleşmeli.
/// Serbest pratik outbox event üretmez; puan/rozet akışına dokunmaz. İstisna: "Günün soruları" (issue #99) oturumu —
/// <c>next</c> setin sabit sorularını sırayla verir, cevaplar <see cref="AnswerSubmittedEvent"/> ile mevcut puan/rozet
/// hattını besler (bkz. <see cref="SubmitDailyAnswerAsync"/>).
/// </summary>
public class PracticeSessionService : IPracticeSessionService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;

    // Client'a ulaşan hata metinleri buradan gelir (issue #184). DI her zaman gerçek localizer'ı
    // verir; parametre yalnızca DI'sız kurulan (birim test) senaryolar için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    // "Günün soruları" (issue #99) gün sınırı: set günü = bugün mü, serbest pratikte bugünün seti dışlaması.
    private readonly ILocalDayCalendar _calendar;

    public PracticeSessionService(AppDbContext context, IStringLocalizer<Messages>? localizer = null, ILocalDayCalendar? calendar = null)
    {
        _context = context;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _calendar = calendar ?? LocalDayCalendar.Default;
    }

    public async Task<PracticeSessionDto> StartAsync(StudentProfileDto student, PracticeSessionStartDto request, CancellationToken ct = default)
    {
        if (!student.GradeId.HasValue)
            throw new InvalidOperationException(_localizer["practice.gradeRequired"]);

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

    public async Task<Paged<PracticeSessionDto>> ListAsync(int studentId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // "Günün soruları" (issue #99) oturumları serbest pratik geçmişinde listelenmez: geçmişten normal modda açılıp
        // "Bitir" ile eksik cevapla kapatılamasın; günlük oturuma yalnız api/practice/daily(/start) üzerinden girilir.
        var query = _context.PracticeSessions
            .AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .Where(s => !_context.DailyQuestionSets.Any(d => d.PracticeSessionId == s.Id));

        var totalCount = await query.CountAsync(ct);

        // Sayaçlar SQL'de hesaplanır; soru satırları belleğe çekilmez.
        var rows = await query
            .OrderByDescending(s => s.StartTime)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.GradeId,
                s.StartTime,
                s.EndTime,
                s.Status,
                s.SubjectIdsJson,
                s.TopicIdsJson,
                AnsweredCount = s.Questions.Count(q => q.AnsweredAt != null),
                CorrectCount = s.Questions.Count(q => q.IsCorrect),
                SkippedCount = s.Questions.Count(q => q.IsSkipped)
            })
            .ToListAsync(ct);

        return new Paged<PracticeSessionDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r => new PracticeSessionDto
            {
                Id = r.Id,
                GradeId = r.GradeId,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                Status = r.Status.ToString(),
                SubjectIds = ParseIds(r.SubjectIdsJson),
                TopicIds = ParseIds(r.TopicIdsJson),
                AnsweredCount = r.AnsweredCount,
                CorrectCount = r.CorrectCount,
                SkippedCount = r.SkippedCount
            }).ToList()
        };
    }

    public async Task<PracticeSessionReviewDto?> GetReviewAsync(int sessionId, int studentId, CancellationToken ct = default)
    {
        var summary = await GetAsync(sessionId, studentId, ct);
        if (summary == null)
            return null;

        // Canvas soruları için tam QuestionDto (geometri + şıklar + passage) gerekir;
        // worksheet sonuç ekranındaki TestSessionService.GetCanvasTestResultAsync ile aynı yaklaşım.
        var rows = await _context.PracticeSessionQuestions
            .AsNoTracking()
            .Include(p => p.Question).ThenInclude(q => q.Answers)
            .Include(p => p.Question).ThenInclude(q => q.Passage)
            .Where(p => p.PracticeSessionId == sessionId)
            .OrderBy(p => p.ShownAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        var questions = rows.Select(p => new PracticeSessionReviewQuestionDto
        {
            // Cevaplanmamış (Pending) sorularda doğru şık gizlenir; aksi halde öğrenci /review üzerinden
            // henüz cevaplamadığı canlı bir soruyu gönderim öncesi görebilir (answer leakage).
            Question = MapQuestion(p.Question, revealCorrectAnswer: p.AnsweredAt != null),
            Status = p.AnsweredAt == null ? "Pending" : p.IsSkipped ? "Skipped" : "Answered",
            IsSkipped = p.IsSkipped,
            IsCorrect = p.IsCorrect,
            SelectedAnswerId = p.SelectedAnswerId,
            TimeTaken = p.TimeTaken,
            ShownAt = p.ShownAt,
            AnsweredAt = p.AnsweredAt
        }).ToList();

        return new PracticeSessionReviewDto { Session = summary, Questions = questions };
    }

    public async Task<PracticeNextQuestionDto?> NextQuestionAsync(int sessionId, int studentId, CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, studentId, ct);
        if (session == null)
            return null;

        EnsureActive(session);

        // Günlük set oturumu (issue #99): sorular setin CANLI item'larından (silinmemiş soru, Order sırası) gelir.
        var daily = await GetDailySessionAsync(session.Id, ct);

        // Gösterilmiş ama cevaplanmamış soru varsa (örn. sayfa yenilendi) yenisini seçme, onu döndür. Günlük oturumda
        // canlı listede olmayan (gün içinde silinmiş) bekleyen soru atlanır.
        var pending = session.Questions.FirstOrDefault(q =>
            q.AnsweredAt == null && (daily == null || daily.LiveQuestionIds.Contains(q.QuestionId)));
        int questionId;

        if (pending != null)
        {
            questionId = pending.QuestionId;
        }
        else
        {
            var shownIds = session.Questions.Select(q => q.QuestionId).ToList();

            int? nextId;
            if (daily != null)
            {
                // Havuzdan rastgele değil, setin sıradaki gösterilmemiş sorusu; set bitince havuz bitti cevabı döner.
                nextId = daily.LiveQuestionIds.Where(id => !shownIds.Contains(id)).Select(id => (int?)id).FirstOrDefault();
            }
            else
            {
                // Security O1 (#99): bugünün günlük setinde olup günlük oturumda henüz cevaplanmamış sorular serbest pratikten
                // dışlanır — aksi halde serbest pratik review'u doğru şıkkı gösterip günlük seti "önceden çözdürürdü".
                var reserved = await LoadTodaysUnansweredDailyQuestionIdsAsync(session.StudentId, ct);

                var pool = PracticeQuestionPool.Build(_context, session.GradeId, ParseIds(session.SubjectIdsJson), ParseIds(session.TopicIdsJson))
                    .Where(q => !shownIds.Contains(q.Id) && !reserved.Contains(q.Id));

                var count = await pool.CountAsync(ct);

                // Rastgele seçim: sayım + rastgele offset. MVP ölçeğinde yeterli; tek satır çeker.
                nextId = count == 0
                    ? null
                    : await pool
                        .OrderBy(q => q.Id)
                        .Skip(Random.Shared.Next(count))
                        .Select(q => q.Id)
                        .FirstAsync(ct);
            }

            if (nextId == null)
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

            questionId = nextId.Value;

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

    public Task<PracticeAnswerResultDto?> SubmitAnswerAsync(int sessionId, int studentId, PracticeAnswerSubmitDto dto, CancellationToken ct = default)
        => SubmitAnswerAsync(sessionId, studentId, dto, clientId: null, ct);

    public async Task<PracticeAnswerResultDto?> SubmitAnswerAsync(int sessionId, int studentId, PracticeAnswerSubmitDto dto, string? clientId, CancellationToken ct = default)
    {
        var session = await LoadOwnedSessionAsync(sessionId, studentId, ct);
        if (session == null)
            return null;

        EnsureActive(session);

        var psq = session.Questions.FirstOrDefault(q => q.QuestionId == dto.QuestionId);
        if (psq == null)
            throw new InvalidOperationException(_localizer["practice.questionNotShown"]);

        if (psq.AnsweredAt != null)
            throw new InvalidOperationException(_localizer["practice.questionAlreadyAnswered"]);

        var question = await _context.Questions
            .AsNoTracking()
            .Where(q => q.Id == dto.QuestionId)
            .Select(q => new { q.CorrectAnswerId, AnswerIds = q.Answers.Select(a => a.Id).ToList() })
            .FirstOrDefaultAsync(ct);

        if (question == null)
            throw new InvalidOperationException(_localizer["practice.questionNotFound"]);

        var skipped = dto.Skipped;
        int? selectedAnswerId = null;
        var isCorrect = false;

        if (!skipped)
        {
            if (!dto.SelectedAnswerId.HasValue)
                throw new InvalidOperationException(_localizer["practice.answerOrSkipRequired"]);

            if (!question.AnswerIds.Contains(dto.SelectedAnswerId.Value))
                throw new InvalidOperationException(_localizer["practice.answerNotBelongToQuestion"]);

            selectedAnswerId = dto.SelectedAnswerId;
            isCorrect = question.CorrectAnswerId.HasValue && question.CorrectAnswerId.Value == selectedAnswerId.Value;
        }

        var answeredAt = DateTime.UtcNow;
        var timeTaken = Math.Clamp(dto.TimeTaken, 0, PracticeAnswerSubmitDto.MaxTimeTakenSeconds);

        var daily = await GetDailySessionAsync(session.Id, ct);
        if (daily == null)
        {
            // Serbest pratik (#62): event yok, izlenen entity üzerinden kayıt.
            psq.SelectedAnswerId = selectedAnswerId;
            psq.IsCorrect = isCorrect;
            psq.IsSkipped = skipped;
            psq.TimeTaken = timeTaken;
            psq.AnsweredAt = answeredAt;
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            await SubmitDailyAnswerAsync(session, studentId, daily, psq,
                new DailyAnswer(selectedAnswerId, isCorrect, skipped, timeTaken, answeredAt), clientId, ct);
        }

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

    // --- günlük set (issue #99) ---

    /// <summary>Günlük set oturumu bilgisi: set günü ve CANLI soru id'leri (<see cref="DailySetQueries"/>).</summary>
    private sealed record DailySession(int SetId, DateOnly Day, IReadOnlyList<int> LiveQuestionIds);

    private readonly record struct DailyAnswer(int? SelectedAnswerId, bool IsCorrect, bool Skipped, int TimeTaken, DateTime AnsweredAt);

    /// <summary>Oturum bir günlük sete bağlıysa set bilgisi; değilse null.</summary>
    private async Task<DailySession?> GetDailySessionAsync(int sessionId, CancellationToken ct)
    {
        var set = await _context.DailyQuestionSets
            .AsNoTracking()
            .Where(d => d.PracticeSessionId == sessionId)
            .Select(d => new { d.Id, d.Day })
            .FirstOrDefaultAsync(ct);

        if (set == null)
            return null;

        var live = await DailySetQueries.LoadLiveQuestionIdsAsync(_context, set.Id, ct);
        return new DailySession(set.Id, set.Day, live);
    }

    /// <summary>Bugünün setinde olup günlük oturumda henüz cevaplanmamış soru id'leri (serbest pratik dışlaması, security O1).</summary>
    private async Task<List<int>> LoadTodaysUnansweredDailyQuestionIdsAsync(int studentId, CancellationToken ct)
    {
        var today = _calendar.Today;
        return await _context.DailyQuestionSetItems
            .AsNoTracking()
            .Where(i => i.DailyQuestionSet.StudentId == studentId && i.DailyQuestionSet.Day == today)
            .Where(i => !_context.PracticeSessionQuestions.Any(p =>
                p.PracticeSessionId == i.DailyQuestionSet.PracticeSessionId
                && p.QuestionId == i.QuestionId
                && p.AnsweredAt != null))
            .Select(i => i.QuestionId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Günlük set cevabı (issue #99). Cevap KOŞULLU yazılır (<c>WHERE Id = @id AND AnsweredAt IS NULL</c>; security D1 /
    /// CR U3): eşzamanlı ikinci gönderim 0 satır günceller → <c>questionAlreadyAnswered</c>, event yok. Aynı transaction'da:
    /// <list type="bullet">
    /// <item><see cref="AnswerSubmittedEvent"/> outbox satırı — yalnız pas DEĞİLSE (CR U2 / security D3: seri, sayaç ve gün
    /// aktivitesi pasla şişmesin), set günü BUGÜNSE (security D5: dünkü oturuma bugün verilen cevap seriyi beslemez) ve soru
    /// setin canlı listesindeyse. Cevap kaydı her durumda kalır.</item>
    /// <item>Canlı listenin tüm soruları cevaplandıysa oturum kapanır (set Completed).</item>
    /// </list>
    /// Retry'lı execution strategy içinde; her deneme önceki denemenin outbox entity'sini bırakıp yeniden ekler.
    /// </summary>
    private async Task SubmitDailyAnswerAsync(
        PracticeSession session, int studentId, DailySession daily, PracticeSessionQuestion psq,
        DailyAnswer answer, string? clientId, CancellationToken ct)
    {
        var emitEvent = !answer.Skipped
            && daily.Day == _calendar.Today
            && daily.LiveQuestionIds.Contains(psq.QuestionId);

        var answeredIds = session.Questions
            .Where(q => q.AnsweredAt != null)
            .Select(q => q.QuestionId)
            .Append(psq.QuestionId)
            .ToHashSet();
        var completes = daily.LiveQuestionIds.All(answeredIds.Contains);

        var eventData = emitEvent ? await LoadEventDataAsync(studentId, psq.QuestionId, ct) : null;

        OutboxMessage? outbox = null;
        var strategy = _context.Database.CreateExecutionStrategy();
        var written = await strategy.ExecuteAsync(async () =>
        {
            if (outbox != null)
            {
                _context.Entry(outbox).State = EntityState.Detached;
                outbox = null;
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var affected = await _context.PracticeSessionQuestions
                .Where(p => p.Id == psq.Id && p.AnsweredAt == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.SelectedAnswerId, answer.SelectedAnswerId)
                    .SetProperty(p => p.IsCorrect, answer.IsCorrect)
                    .SetProperty(p => p.IsSkipped, answer.Skipped)
                    .SetProperty(p => p.TimeTaken, answer.TimeTaken)
                    .SetProperty(p => p.AnsweredAt, (DateTime?)answer.AnsweredAt)
                    .SetProperty(p => p.UpdateTime, (DateTime?)answer.AnsweredAt), ct);

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            if (eventData != null)
            {
                outbox = BuildDailyAnswerOutbox(eventData, session.Id, psq.Id, answer, clientId);
                _context.OutboxMessages.Add(outbox);
                await _context.SaveChangesAsync(ct);
            }

            if (completes)
            {
                await _context.PracticeSessions
                    .Where(x => x.Id == session.Id && x.Status == PracticeSessionStatus.Active)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.Status, PracticeSessionStatus.Ended)
                        .SetProperty(x => x.EndTime, (DateTime?)answer.AnsweredAt)
                        .SetProperty(x => x.UpdateTime, (DateTime?)answer.AnsweredAt), ct);
            }

            await tx.CommitAsync(ct);
            return true;
        });

        if (!written)
            throw new InvalidOperationException(_localizer["practice.questionAlreadyAnswered"]);

        // İzlenen entity'leri DB'de yazılan değerlerle eşitle (sonraki SaveChanges tekrar yazmasın).
        psq.SelectedAnswerId = answer.SelectedAnswerId;
        psq.IsCorrect = answer.IsCorrect;
        psq.IsSkipped = answer.Skipped;
        psq.TimeTaken = answer.TimeTaken;
        psq.AnsweredAt = answer.AnsweredAt;
        psq.UpdateTime = answer.AnsweredAt;
        _context.Entry(psq).State = EntityState.Unchanged;

        if (completes && session.Status == PracticeSessionStatus.Active)
        {
            session.Status = PracticeSessionStatus.Ended;
            session.EndTime = answer.AnsweredAt;
            session.UpdateTime = answer.AnsweredAt;
            _context.Entry(session).State = EntityState.Unchanged;
        }
    }

    private sealed record DailyEventData(
        int UserId, int QuestionId, int? SubjectId, string? SubjectName, int? TopicId, int? SubTopicId, int Point, int DifficultyLevel);

    private async Task<DailyEventData> LoadEventDataAsync(int studentId, int questionId, CancellationToken ct)
    {
        var userId = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.UserId)
            .FirstAsync(ct);

        return await _context.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new DailyEventData(
                userId,
                q.Id,
                q.SubjectId,
                q.Subject != null ? q.Subject.Name : null,
                q.TopicId,
                q.QuestionSubTopics.OrderBy(st => st.Id).Select(st => (int?)st.SubTopicId).FirstOrDefault(),
                q.Point,
                q.DifficultyLevel))
            .FirstAsync(ct);
    }

    /// <summary>
    /// Günlük set cevabının <see cref="AnswerSubmittedEvent"/> outbox satırı.
    /// <para>
    /// Anahtar: BadgeService puan tekilleştirmesi (<c>AnswerPointAward</c>) (TestInstanceId, QuestionId) anahtarlıdır ve kayıt
    /// tek kullanıcıya aittir. Pratik oturumunun worksheet instance'ı yok; <c>TestInstanceId = -PracticeSessionId</c>
    /// verilir — gerçek instance id'leri pozitif olduğundan çakışmaz, her oturum tek öğrenciye ait olduğundan kullanıcı
    /// uyuşmazlığı oluşmaz ve aynı soru başka gün/oturumda yeniden puanlanabilir (worksheet instance mantığıyla tutarlı).
    /// <c>TestInstanceQuestionId</c> = <c>PracticeSessionQuestion.Id</c>. Cevap koşullu yazımla tek seferliktir →
    /// <c>Revision = 1</c>. BadgeService tarafında değişiklik gerekmez.
    /// </para>
    /// </summary>
    private static OutboxMessage BuildDailyAnswerOutbox(
        DailyEventData data, int sessionId, int psqId, DailyAnswer answer, string? clientId)
    {
        var outboxId = Guid.NewGuid();
        var evt = new AnswerSubmittedEvent
        {
            EventId = outboxId,
            UserId = data.UserId,
            QuestionId = data.QuestionId,
            SubjectId = data.SubjectId,
            Subject = data.SubjectName ?? string.Empty,
            TopicId = data.TopicId,
            SubTopicId = data.SubTopicId,
            TestInstanceId = DailyAnswerEventTestInstanceId(sessionId),
            TestInstanceQuestionId = psqId,
            ClientId = clientId ?? string.Empty,
            SelectedAnswerId = answer.SelectedAnswerId,
            IsCorrect = answer.IsCorrect,
            QuestionPoint = data.Point,
            DifficultyLevel = data.DifficultyLevel,
            TimeTakenInSeconds = Math.Clamp(answer.TimeTaken, 0, PracticeAnswerSubmitDto.MaxTimeTakenSeconds),
            SubmittedAt = answer.AnsweredAt,
            Revision = 1
        };

        return new OutboxMessage
        {
            Id = outboxId,
            Type = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>(),
            Content = JsonSerializer.Serialize(evt),
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Günlük set cevabının event'teki <c>TestInstanceId</c>'si — bkz. <see cref="BuildDailyAnswerOutbox"/>.</summary>
    internal static int DailyAnswerEventTestInstanceId(int practiceSessionId) => -practiceSessionId;

    // --- helpers ---

    private Task<PracticeSession?> LoadOwnedSessionAsync(int sessionId, int studentId, CancellationToken ct) =>
        _context.PracticeSessions
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == studentId, ct);

    private void EnsureActive(PracticeSession session)
    {
        if (session.Status != PracticeSessionStatus.Active)
            throw new InvalidOperationException(_localizer["practice.sessionAlreadyEnded"]);
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

    /// <summary>
    /// Canlı akış (<see cref="NextQuestionAsync"/>) için: doğru cevap bilinçli olarak dahil edilmez;
    /// cevap sonrası <see cref="PracticeAnswerResultDto"/> ile döner.
    /// </summary>
    private static QuestionDto MapQuestion(Question q) => MapQuestion(q, revealCorrectAnswer: false);

    /// <summary>
    /// <paramref name="revealCorrectAnswer"/> yalnızca soru zaten cevaplanmış/pas geçilmişse true olmalı
    /// (bkz. <see cref="GetReviewAsync"/>). True ise <see cref="QuestionDto.CorrectAnswerId"/> ve eşleşen
    /// <see cref="AnswerDto.IsCorrect"/> doldurulur; aksi halde ikisi de boş kalır.
    /// </summary>
    private static QuestionDto MapQuestion(Question q, bool revealCorrectAnswer) => new()
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
        CorrectAnswerId = revealCorrectAnswer ? q.CorrectAnswerId : null,
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
                Order = a.Order,
                IsCorrect = revealCorrectAnswer && q.CorrectAnswerId.HasValue && q.CorrectAnswerId.Value == a.Id
            })
            .ToList()
    };
}
