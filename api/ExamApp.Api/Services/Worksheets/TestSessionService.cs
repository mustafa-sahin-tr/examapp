using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.Localization;
using System.Text.Json;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// A student's test-taking session: starting an instance, reading its questions/results,
/// saving answers (which emits the AnswerSubmitted outbox event) and ending the test.
/// Extracted from ExamService.
/// </summary>
public class TestSessionService : ITestSessionService
{
    private readonly AppDbContext _context;

    // Client'a donen mesajlar (ResponseBaseDto.Message ve istemciye sizan exception metinleri)
    // buradan gelir (issue #184). Log mesajlari cevrilmez. DI her zaman gercek localizer'i
    // verir; parametre yalnizca DI'siz kurulan (birim test) senaryolar icin opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public TestSessionService(AppDbContext context, IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(StudentProfileDto student, int pageNumber, int pageSize)
    {
        var query = await _context.TestInstances
            .Where(wi => wi.StudentId == student.Id && wi.Status == WorksheetInstanceStatus.Completed)
            .Select(wi => new
            {
                wi.Id,
                wi.Worksheet.Name,
                wi.Worksheet.ImageUrl,
                wi.EndTime,
                wi.StartTime,
                TotalQuestions = wi.WorksheetInstanceQuestions.Count(),

                CorrectAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId &&
                        wq.Question.CorrectAnswerId == wiq.SelectedAnswerId)),

                WrongAnswers = wi.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null &&
                    wi.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId &&
                        wq.Question.CorrectAnswerId != wiq.SelectedAnswerId))
            })
            .ToListAsync();

        var results = query.Select(wi => new InstanceSummaryDto
        {
            Id = wi.Id,
            Name = wi.Name,
            ImageUrl = wi.ImageUrl,
            CompletedDate = wi.EndTime ?? DateTime.UtcNow,
            DurationMinutes = wi.EndTime.HasValue ?
                (int)(wi.EndTime.Value - wi.StartTime).TotalMinutes : 0,
            TotalQuestions = wi.TotalQuestions,
            CorrectAnswers = wi.CorrectAnswers,
            WrongAnswers = wi.WrongAnswers,
            Score = (wi.CorrectAnswers * 100) / (wi.TotalQuestions > 0 ? wi.TotalQuestions : 1)
        })
        .OrderByDescending(wi => wi.CompletedDate)
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .ToList();

        return new Paged<InstanceSummaryDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = query.Count,
            Items = results
        };
    }

    public async Task<TestStartResultDto> StartTestAsync(int testId, StudentProfileDto student)
    {
        var worksheet = await _context.Worksheets
            .Where(w => w.Id == testId)
            .Select(w => new { w.Id, w.GradeId, w.StudentVisibility })
            .FirstOrDefaultAsync();

        if (worksheet == null)
        {
            return null!;
        }

        var now = DateTime.UtcNow;

        var hasActiveAssignment = await _context.ActiveAssignmentsFor(student.Id, student.GradeId, student.SchoolId, now)
            .AnyAsync(a => a.WorksheetId == testId);

        var isGradeMatch = student.GradeId.HasValue && worksheet.GradeId == student.GradeId.Value;

        if (!WorksheetAccess.CanStudentStartTest(hasActiveAssignment, isGradeMatch, worksheet.StudentVisibility))
        {
            throw new UnauthorizedAccessException(_localizer["worksheets.session.accessDenied"]);
        }

        // issue #367 (security review): tekrar çözüm yok. Eskiden arama `EndTime == null` ileydi; EndTest her zaman
        // EndTime yazdığı için tamamlanmış test hiç bulunamıyor, start-test yeni bir Started instance açıyordu (aşağıdaki
        // alreadyCompleted dalı ölüydü) ve BadgeService (TestInstanceId, QuestionId) anahtarıyla puanı yeniden veriyordu.
        // Artık karar Status'e göre: Started → devam; başka her durum → alreadyCompleted (yeni instance yok).
        var existing = await ExistingInstanceResultAsync(student.Id, testId);
        if (existing != null)
            return existing;

        var instance = new WorksheetInstance
        {
            WorksheetId = testId,
            StudentId = student.Id,
            Status = WorksheetInstanceStatus.Started,
            WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>(),
            StartTime = DateTime.UtcNow
        };

        // Teste ait soruları TestQuestion tablosundan çekiyoruz
        var testQuestions = await _context.TestQuestions
            .Where(tq => tq.TestId == testId)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.QuestionSubTopics)
            .OrderBy(tq => tq.Order)
            .ThenBy(tq => tq.Id) // issue #309: eşit Order'da kararlı sıra (WorksheetQuestionNumbering ile aynı)
            .ToListAsync();

        foreach (var tq in testQuestions)
        {
            instance.WorksheetInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetQuestionId = tq.Id,
                IsCorrect = false,
                TimeTaken = 0
            });
        }

        _context.TestInstances.Add(instance);
        try
        {
            await _context.SaveChangesAsync(); // burada audit çalışır
        }
        catch (DbUpdateException ex) when (IsDuplicateLiveInstanceViolation(ex))
        {
            // issue #367: eşzamanlı ikinci start-test (çift tık / iki sekme) IX_TestInstances_StudentId_WorksheetId'ye
            // düştü: bizimkini bırak, kazananın instance'ını döndür (DailyQuestionSetService ile aynı desen).
            foreach (var tiq in instance.WorksheetInstanceQuestions.ToList())
                _context.Entry(tiq).State = EntityState.Detached;
            _context.Entry(instance).State = EntityState.Detached;

            return await ExistingInstanceResultAsync(student.Id, testId)
                ?? throw new InvalidOperationException("Test instance unique violation but no existing instance found.");
        }

        return new TestStartResultDto
        {
            Success = true,
            InstanceId = instance.Id,
            StartTime = instance.StartTime
        };
    }

    /// <summary>Unique index adı — <see cref="AppDbContext"/> (StudentId, WorksheetId) filtreli unique index'i.</summary>
    internal const string LiveInstanceUniqueIndexName = "IX_TestInstances_StudentId_WorksheetId";

    /// <summary>
    /// issue #367 (security Low-3): yalnız (StudentId, WorksheetId) unique index'inin ihlali "kazananı oku" yoluna girer;
    /// başka bir unique ihlali (ör. soru satırları) yukarı fırlar. Postgres: <c>ConstraintName</c>; SQLite (yalnız birim
    /// testleri) constraint adı vermez, mesajdaki sütun listesine bakılır.
    /// </summary>
    internal static bool IsDuplicateLiveInstanceViolation(DbUpdateException ex) =>
        DbUpdateExceptionClassifier.IsUniqueViolation(ex)
        && ex.InnerException switch
        {
            Npgsql.PostgresException pg => pg.ConstraintName == LiveInstanceUniqueIndexName,
            var other => other?.Message.Contains("TestInstances.StudentId, TestInstances.WorksheetId", StringComparison.Ordinal) == true
        };

    /// <summary>
    /// issue #367: öğrencinin bu worksheet için canlı (silinmemiş) instance'ı varsa start-test yanıtı; yoksa null.
    /// Started olmayan (Completed/Expired) bir instance her zaman önceliklidir — eski veride hem tamamlanmış hem açık
    /// (tekrar çözüm) instance kalmışsa açık olan devam ettirilmez, yine alreadyCompleted döner.
    /// </summary>
    private async Task<TestStartResultDto?> ExistingInstanceResultAsync(int studentId, int testId)
    {
        var existing = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.StudentId == studentId && ti.WorksheetId == testId)
            .OrderBy(ti => ti.Status == WorksheetInstanceStatus.Started ? 1 : 0)
            .ThenByDescending(ti => ti.Id)
            .Select(ti => new { ti.Id, ti.Status, ti.StartTime })
            .FirstOrDefaultAsync();

        if (existing == null)
            return null;

        if (existing.Status != WorksheetInstanceStatus.Started)
        {
            return new TestStartResultDto
            {
                Success = false,
                Message = _localizer["worksheets.session.alreadyCompleted"],
                InstanceId = existing.Id,
                StartTime = existing.StartTime
            };
        }

        return new TestStartResultDto
        {
            Success = true,
            InstanceId = existing.Id,
            StartTime = existing.StartTime
        };
    }

    public async Task<WorksheetInstanceDto?> GetTestInstanceQuestionsAsync(int testInstanceId, int userId)
    {
        var instance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(wq => wq.Question)
                    .ThenInclude(q => q.Answers)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(wq => wq.Question)
                    .ThenInclude(q => q.Passage)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (instance == null)
            return null;

        var worksheetInstanceDto = new WorksheetInstanceDto
        {
            Id = instance.Id,
            TestName = instance.Worksheet.Name,
            Status = instance.Status,
            MaxDurationSeconds = instance.Worksheet.MaxDurationSeconds,
            IsPracticeTest = instance.Worksheet.IsPracticeTest,
            // issue #309: açık sıra — kaynak WorksheetQuestion'ın (Order, Id)'si (instance satırının kendi sıra alanı yok).
            TestInstanceQuestions = OrderedForDisplay(instance.WorksheetInstanceQuestions).Select(tiq => new WorksheetInstanceQuestionDto
            {
                Id = tiq.Id,
                Order = tiq.WorksheetQuestion.Order,
                SelectedAnswerId = tiq.SelectedAnswerId,
                Question = new QuestionDto
                {
                    Id = tiq.WorksheetQuestion.Question.Id,
                    Text = tiq.WorksheetQuestion?.Question?.Text ?? string.Empty,
                    SubText = tiq.WorksheetQuestion?.Question?.SubText,
                    ImageUrl = tiq.WorksheetQuestion?.Question?.ImageUrl,
                    IsExample = tiq.WorksheetQuestion?.Question?.IsExample ?? false,
                    PracticeCorrectAnswer = tiq.WorksheetQuestion?.Question?.PracticeCorrectAnswer,
                    AnswerColCount = tiq.WorksheetQuestion?.Question?.AnswerColCount ?? 0,
                    Passage = tiq.WorksheetQuestion?.Question?.PassageId != null
                        ? new PassageDto
                        {
                            Id = tiq.WorksheetQuestion.Question.Passage!.Id,
                            Title = tiq.WorksheetQuestion.Question.Passage.Title,
                            Text = tiq.WorksheetQuestion.Question.Passage.Text,
                            ImageUrl = tiq.WorksheetQuestion.Question.Passage.ImageUrl
                        }
                        : null,
                    Answers = tiq.WorksheetQuestion?.Question?.Answers?.Select(a => new AnswerDto
                    {
                        Id = a.Id,
                        Text = a.Text,
                        ImageUrl = a.ImageUrl,
                        Tag = a.Tag,
                        Order = a.Order
                    })?.ToList() ?? new List<AnswerDto>()
                }
            }).ToList()
        };

        foreach (var question in worksheetInstanceDto.TestInstanceQuestions)
        {
            var dto = question.Question;
            var fallbackColumns = dto.AnswerColCount > 0
                ? dto.AnswerColCount
                : Math.Max(1, Math.Min(dto.Answers?.Count ?? 0, 4));
        }

        return worksheetInstanceDto;
    }

    public async Task<WorksheetInstanceResultDto?> GetCanvasTestResultAsync(int testInstanceId, int userId, bool includeCorrectAnswer = false)
    {
        var testInstance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Answers)
            .Include(ti => ti.WorksheetInstanceQuestions)
                .ThenInclude(tiq => tiq.WorksheetQuestion)
                .ThenInclude(tq => tq.Question)
                .ThenInclude(q => q.Passage)
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId &&
                    ti.Student.UserId == userId);

        if (testInstance == null)
        {
            return null;
        }

        if (includeCorrectAnswer && testInstance.Status != WorksheetInstanceStatus.Completed)
        {
            return null;
        }

        var response = new WorksheetInstanceResultDto
        {
            Id = testInstance.Id,
            WorksheetId = testInstance.WorksheetId,
            TestName = testInstance.Worksheet.Name,
            Status = testInstance.Status,
            MaxDurationSeconds = testInstance.Worksheet.MaxDurationSeconds,
            IsPracticeTest = testInstance.Worksheet.IsPracticeTest,
            TestInstanceQuestions = OrderedForDisplay(testInstance.WorksheetInstanceQuestions).Select(tiq =>
            {
                var questionEntity = tiq.WorksheetQuestion.Question;
                var questionDto = new QuestionDto
                {
                    Id = questionEntity.Id,
                    Text = questionEntity?.Text ?? string.Empty,
                    SubText = questionEntity.SubText,
                    ImageUrl = questionEntity.ImageUrl,
                    IsExample = questionEntity.IsExample,
                    InteractionType = questionEntity.InteractionType,
                    InteractionPlan = questionEntity.InteractionPlan,
                    ShowPassageFirst = questionEntity.ShowPassageFirst,
                    CorrectAnswerId = includeCorrectAnswer ? questionEntity.CorrectAnswerId : null,
                    Passage = questionEntity.PassageId.HasValue ? new PassageDto
                    {
                        Id = questionEntity.Passage?.Id,
                        Title = questionEntity.Passage?.Title,
                        Text = questionEntity.Passage?.Text,
                        ImageUrl = questionEntity.Passage?.ImageUrl,
                        X = questionEntity.Passage?.X,
                        Y = questionEntity.Passage?.Y,
                        Width = questionEntity.Passage?.Width,
                        Height = questionEntity.Passage?.Height
                    } : null,
                    PracticeCorrectAnswer = questionEntity.PracticeCorrectAnswer,
                    AnswerColCount = questionEntity.AnswerColCount,
                    IsCanvasQuestion = questionEntity.IsCanvasQuestion,
                    X = questionEntity.X,
                    Y = questionEntity.Y,
                    Width = questionEntity.Width,
                    Height = questionEntity.Height,
                    SanitizedHeight = questionEntity.SanitizedHeight,
                    Answers = questionEntity.Answers.Select(a => new AnswerDto
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
                    }).ToList()
                };

                var fallbackColumns = questionDto.AnswerColCount > 0
                    ? questionDto.AnswerColCount
                    : Math.Max(1, Math.Min(questionDto.Answers.Count, 4));


                return new WorksheetInstanceQuestionDto
                {
                    Id = tiq.Id,
                    Order = tiq.WorksheetQuestion.Order,
                    Question = questionDto,
                    SelectedAnswerId = tiq.SelectedAnswerId,
                    AnswerPayload = tiq.AnswerPayload,
                    TimeTaken = tiq.TimeTaken
                };
            }).ToList()
        };


        return response;
    }

    /// <summary>
    /// issue #309: UI soru numarasını bu listenin konumundan (index + 1) üretir; sıra kaynak <see cref="WorksheetQuestion"/>'ın
    /// <c>(Order, Id)</c>'si — <see cref="Helpers.WorksheetQuestionNumbering"/> ile aynı kural. Önceden açık sıra yoktu
    /// (Include'un döndüğü sıra); fark yalnız eşit Order'da görülür.
    /// </summary>
    private static IEnumerable<WorksheetInstanceQuestion> OrderedForDisplay(IEnumerable<WorksheetInstanceQuestion> questions) =>
        questions
            .OrderBy(tiq => tiq.WorksheetQuestion.Order)
            .ThenBy(tiq => tiq.WorksheetQuestion.Id);

    public async Task<TestSessionResultDto> SaveAnswer(SaveAnswerDto dto, UserProfileDto user, CancellationToken ct = default)
    {
        // issue #279 review (critical fix): exam API's AppDbContext is registered via Aspire's
        // AddNpgsqlDbContext, which enables Npgsql retry-on-failure by default (NpgsqlRetryingExecutionStrategy,
        // see Program.cs). A user-initiated transaction (Database.BeginTransactionAsync) opened OUTSIDE
        // Database.CreateExecutionStrategy().ExecuteAsync(...) throws InvalidOperationException at runtime
        // ("...does not support user-initiated transactions") — every SaveAnswer call would fail against
        // Postgres in production (SQLite tests don't enable a retrying strategy, so they didn't catch this).
        // Same pattern as QuestionService.SaveBulkQuestion / TeacherService (TeacherService.cs ~L230): the
        // ENTIRE retriable unit — load, atomic revision increment, re-select, field mutations, outbox insert,
        // SaveChanges, commit — runs inside the strategy delegate. ChangeTracker.Clear() at the top of the
        // delegate discards any tracked state from a prior failed attempt (the strategy may invoke this
        // delegate more than once on the SAME DbContext instance for a transient failure) so every attempt
        // starts from a clean, freshly-reloaded state — no entity is loaded or mutated outside the delegate.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async _ =>
        {
            _context.ChangeTracker.Clear();

            var testInstanceQuestion = await _context.TestInstanceQuestions
                        .Include(t => t.WorksheetQuestion)
                            .ThenInclude(wq => wq.Question)
                                .ThenInclude(q => q.Subject)
                        .Include(t => t.WorksheetQuestion)
                            .ThenInclude(wq => wq.Question)
                                .ThenInclude(q => q.QuestionSubTopics)
                .FirstOrDefaultAsync(tiq => tiq.WorksheetInstanceId == dto.TestInstanceId &&
                    tiq.Id == dto.TestQuestionId
                    && tiq.WorksheetInstance.Student.UserId == user.Id, ct);

            if (testInstanceQuestion == null)
            {
                return new TestSessionResultDto
                {
                    Success = false,
                    Message = _localizer["worksheets.session.instanceQuestionNotFound"]
                };
            }

            var question = testInstanceQuestion.WorksheetQuestion?.Question;
            if (question == null)
            {
                return new TestSessionResultDto
                {
                    Success = false,
                    Message = _localizer["worksheets.session.questionDataNotFound"]
                };
            }

            // issue #279 review: see the AnswerRevision comment below for why the transaction exists at all.
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // issue #367: the instance must still be Started — otherwise a student could finish the test, read the
            // correct answers (only exposed once Completed) and rewrite wrong answers into right ones, each rewrite
            // emitting a fresh AnswerSubmittedEvent (points/badges). The status is checked by a conditional no-op
            // UPDATE on the instance row as the FIRST statement of the write transaction, not by a prior read:
            // the UPDATE takes the row's write lock until COMMIT, and EndTest's UPDATE of the same row (Status →
            // Completed) needs that lock too, so the two serialize:
            //   - SaveAnswer first → EndTest waits for our commit; the answer lands before completion (in-flight
            //     answer sent right before "finish" is kept — #383 awaits it before calling end-test).
            //   - EndTest first → our UPDATE waits, Postgres re-evaluates the WHERE on the committed row (READ
            //     COMMITTED), sees Completed → 0 rows → reject. Nothing below runs: no answer write, no outbox row.
            // A plain SELECT of Status here would not lock and could read Started just before EndTest commits.
            var stillInProgress = await _context.TestInstances
                .Where(ti => ti.Id == testInstanceQuestion.WorksheetInstanceId
                    && ti.Status == WorksheetInstanceStatus.Started)
                .ExecuteUpdateAsync(s => s.SetProperty(ti => ti.Status, ti => ti.Status), ct);

            if (stillInProgress == 0)
            {
                await transaction.RollbackAsync(ct);
                _context.ChangeTracker.Clear();
                return new TestSessionResultDto
                {
                    Success = false,
                    Conflict = true,
                    ErrorCode = TestSessionErrorCodes.TestNotInProgress,
                    Message = _localizer["worksheets.session.answerRejectedNotInProgress"]
                };
            }

            // Store MCQ selection and/or structured answer payload
            testInstanceQuestion.SelectedAnswerId = dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null;
            testInstanceQuestion.AnswerPayload = string.IsNullOrWhiteSpace(dto.AnswerPayload) ? null : dto.AnswerPayload;
            testInstanceQuestion.TimeTaken = dto.TimeTaken;

            var interactionType = question.InteractionType ?? "mcq";
            var isDragDropLabeling = interactionType.Equals("dragDropLabeling", StringComparison.OrdinalIgnoreCase);

            bool isCorrect;
            if (isDragDropLabeling)
            {
                // For dragDropLabeling, correctness is determined client-side for practice (IsExample)
                // and server-side evaluation can be added later. For now, we store payload and mark unknown as false.
                isCorrect = false;
            }
            else
            {
                var correctAnswerId = question.CorrectAnswerId;
                isCorrect = correctAnswerId.HasValue && correctAnswerId.Value == dto.SelectedAnswerId;
            }
            testInstanceQuestion.IsCorrect = isCorrect;

            var primarySubTopicId = question.QuestionSubTopics?.FirstOrDefault()?.SubTopicId;

            // issue #279 review (bugfix found while implementing the blocker below): `testInstanceQuestion` was
            // loaded via a TRACKING query above, so the field mutations already made it (SelectedAnswerId/
            // AnswerPayload/TimeTaken/IsCorrect) are automatically picked up as Modified by SaveChangesAsync — an
            // explicit `.Update(testInstanceQuestion)` here is not just redundant, it's actively HARMFUL: `Update()`
            // marks EVERY scalar property as Modified using its currently-tracked (stale) in-memory value, which
            // includes AnswerRevision — so SaveChangesAsync would silently overwrite the atomic increment performed
            // via ExecuteUpdateAsync below back to its pre-increment value. Removed.

            // issue #279 review (blocker + security M1/L3): AnswerRevision must be a DB-generated, atomically
            // incrementing counter — not the tracked entity's in-memory value (`+= 1` on a loaded entity is a
            // read-modify-write race: two concurrent SaveAnswer calls for the SAME row could both read
            // revision N and both compute N+1, so the row ends up at N+1 instead of N+2 and one of the two
            // AnswerSubmittedEvents ends up carrying a revision the OTHER call's write also carries — the
            // BadgeService consumer would then treat the second-committing one as stale even though it's a
            // genuinely distinct answer).
            //
            // Fix: a single atomic `UPDATE ... SET "AnswerRevision" = "AnswerRevision" + 1` (EF's
            // ExecuteUpdateAsync — translates to one round trip, Postgres/SQLite both evaluate the increment
            // server-side under the row's write lock, so two concurrent callers serialize and each gets a
            // DISTINCT, strictly-increasing value) run INSIDE an explicit transaction that also covers the
            // rest of this method's SaveChangesAsync (answer fields + outbox row) — so the atomic increment
            // and the answer/outbox write commit or roll back together, preserving the outbox pattern's
            // "same transaction" guarantee. The immediate follow-up SELECT safely reads our own increment
            // (Postgres/SQLite: our transaction holds the row's write lock until COMMIT, so no other
            // transaction can interleave between our UPDATE and this SELECT). The transaction itself is opened
            // above, before the issue #367 status lock, so the lock, this increment and the outbox insert are one unit.
            await _context.TestInstanceQuestions
                .Where(x => x.Id == testInstanceQuestion.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AnswerRevision, x => x.AnswerRevision + 1), ct);

            var newRevision = await _context.TestInstanceQuestions
                .Where(x => x.Id == testInstanceQuestion.Id)
                .Select(x => x.AnswerRevision)
                .FirstAsync(ct);

            // 1. Event oluştur
            // EventId = outbox satırının Id'si (LoginAttemptedEvent ile aynı desen, issue #243) —
            // BadgeService bu satırı işlenmiş sayıp aynı SaveChanges'te idempotency defterine yazar.
            var outboxId = Guid.NewGuid();
            var evt = new AnswerSubmittedEvent
            {
                EventId = outboxId,
                UserId = user.Id,
                QuestionId = question.Id,
                SubjectId = question.SubjectId,
                Subject = question.Subject?.Name ?? string.Empty,
                QuestionPoint = question.Point,
                DifficultyLevel = question.DifficultyLevel,
                SubmittedAt = DateTime.UtcNow,
                TimeTakenInSeconds = dto.TimeTaken,
                ClientId = user.KeycloakId,
                IsCorrect = isCorrect,
                SubTopicId = primarySubTopicId,
                TopicId = question.TopicId,
                TestInstanceId = dto.TestInstanceId,
                TestInstanceQuestionId = testInstanceQuestion.Id,
                Revision = newRevision,
                SelectedAnswerId = dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null
            };

            // 2. Outbox'a yaz
            var outbox = new OutboxMessage
            {
                Id = outboxId,
                Type = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>(),
                Content = JsonSerializer.Serialize(evt),
                CreatedAt = DateTime.UtcNow
            };
            _context.OutboxMessages.Add(outbox);


            // Update Question Count
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new TestSessionResultDto
            {
                Success = true,
                Message = _localizer["worksheets.session.answerSaved"]
            };
        }, ct);
    }

    /// <summary>
    /// Ends a Started instance. issue #367: idempotent — a repeated call (double click, UI retry, two tabs) on an
    /// already Completed instance succeeds without touching it (EndTime stays the first completion's). Only a
    /// non-Completed terminal state (Expired) is a conflict.
    /// <para>
    /// The Started → Completed write is a conditional UPDATE (<c>WHERE Status = Started</c>), so two concurrent calls
    /// cannot both stamp EndTime, and it locks the instance row — the same lock <see cref="SaveAnswer"/> takes before
    /// writing, which is what serializes "complete" against an in-flight answer (see the comment there).
    /// </para>
    /// </summary>
    public async Task<TestSessionResultDto> EndTest(int testInstanceId, int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // ExecuteUpdate bypasses the SaveChanges audit hook, so UpdateTime/UpdateUserId are stamped here (the caller
        // is the instance's student — ownership is part of the WHERE).
        var completed = await _context.TestInstances
            .Where(ti => ti.Id == testInstanceId
                && ti.Student.UserId == userId
                && ti.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Completed)
                .SetProperty(ti => ti.EndTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

        if (completed == 1)
        {
            return new TestSessionResultDto
            {
                Success = true,
                Message = _localizer["worksheets.session.ended"]
            };
        }

        var status = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.Id == testInstanceId && ti.Student.UserId == userId)
            .Select(ti => (WorksheetInstanceStatus?)ti.Status)
            .FirstOrDefaultAsync(ct);

        return status switch
        {
            null => new TestSessionResultDto
            {
                Success = false,
                Message = _localizer["worksheets.session.instanceNotFound"]
            },
            WorksheetInstanceStatus.Completed => new TestSessionResultDto
            {
                Success = true,
                Message = _localizer["worksheets.session.ended"]
            },
            _ => new TestSessionResultDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = TestSessionErrorCodes.TestNotInProgress,
                Message = _localizer["worksheets.session.notInProgress"]
            }
        };
    }
}
