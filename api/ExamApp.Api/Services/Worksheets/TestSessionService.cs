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

        var existing = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.StudentId == student.Id && ti.WorksheetId == testId
                && ti.EndTime == null);


        if (existing != null)
        {
            if (existing.Status == WorksheetInstanceStatus.Completed)
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
        await _context.SaveChangesAsync(); // burada audit çalışır

        return new TestStartResultDto
        {
            Success = true,
            InstanceId = instance.Id,
            StartTime = instance.StartTime
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
            TestInstanceQuestions = instance.WorksheetInstanceQuestions.Select(tiq => new WorksheetInstanceQuestionDto
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
            TestName = testInstance.Worksheet.Name,
            Status = testInstance.Status,
            MaxDurationSeconds = testInstance.Worksheet.MaxDurationSeconds,
            IsPracticeTest = testInstance.Worksheet.IsPracticeTest,
            TestInstanceQuestions = testInstance.WorksheetInstanceQuestions.Select(tiq =>
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

    public async Task<ResponseBaseDto> SaveAnswer(SaveAnswerDto dto, UserProfileDto user)
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
        return await strategy.ExecuteAsync(async () =>
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
                    && tiq.WorksheetInstance.Student.UserId == user.Id);

            if (testInstanceQuestion == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["worksheets.session.instanceQuestionNotFound"]
                };
            }
            // Store MCQ selection and/or structured answer payload
            testInstanceQuestion.SelectedAnswerId = dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null;
            testInstanceQuestion.AnswerPayload = string.IsNullOrWhiteSpace(dto.AnswerPayload) ? null : dto.AnswerPayload;
            testInstanceQuestion.TimeTaken = dto.TimeTaken;

            var question = testInstanceQuestion.WorksheetQuestion?.Question;
            if (question == null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = _localizer["worksheets.session.questionDataNotFound"]
                };
            }

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
            // transaction can interleave between our UPDATE and this SELECT).
            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.TestInstanceQuestions
                .Where(x => x.Id == testInstanceQuestion.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AnswerRevision, x => x.AnswerRevision + 1));

            var newRevision = await _context.TestInstanceQuestions
                .Where(x => x.Id == testInstanceQuestion.Id)
                .Select(x => x.AnswerRevision)
                .FirstAsync();

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
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return new ResponseBaseDto
            {
                Success = true,
                Message = _localizer["worksheets.session.answerSaved"]
            };
        });
    }

    public async Task<ResponseBaseDto> EndTest(int testInstanceId, int userId)
    {
        var testInstance = await _context.TestInstances
            .FirstOrDefaultAsync(ti => ti.Id == testInstanceId && ti.Student.UserId == userId);

        if (testInstance == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = _localizer["worksheets.session.instanceNotFound"]
            };
        }

        if (testInstance.Status != WorksheetInstanceStatus.Started)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = $"Bu test zaten {testInstance.Status} durumunda."
            };
        }

        testInstance.Status = WorksheetInstanceStatus.Completed;
        testInstance.EndTime = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new ResponseBaseDto
        {
            Success = true,
            Message = _localizer["worksheets.session.ended"]
        };
    }
}
