using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
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

    // issue #423: veli bildirimi için sub/ad çözümü (opsiyonel; DI verir, DI'siz birim testler null bırakır → ad boş, event yine yazılır).
    private readonly IAuthApiClient? _authApi;

    public TestSessionService(AppDbContext context, IStringLocalizer<Messages>? localizer = null, IAuthApiClient? authApi = null)
    {
        _context = context;
        _authApi = authApi;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(StudentProfileDto student, int pageNumber, int pageSize)
    {
        var query = await _context.TestInstances
            // issue #396: öğrencinin kendi geçmişi bitmiş oturumları listeler (Completed + Expired) — süresi dolan test de
            // sonuç sayfasına buradan ulaşılır. Puan, cevaplananlar üzerinden aynı formülle hesaplanır.
            .Where(wi => wi.StudentId == student.Id
                && (wi.Status == WorksheetInstanceStatus.Completed || wi.Status == WorksheetInstanceStatus.Expired))
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
            .Select(w => new { w.Id, w.GradeId, w.StudentVisibility, w.MaxDurationSeconds })
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
            StartTime = DateTime.UtcNow,
            // issue #396: süre sınırının kopyası — öğretmen worksheet süresini sonradan değiştirse de bu oturum etkilenmez.
            MaxDurationSeconds = worksheet.MaxDurationSeconds
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
    /// issue #396: süresi dolmuş Started instance burada Expired'a çekilir ve alreadyCompleted döner (devam/tekrar yok).
    /// </summary>
    private async Task<TestStartResultDto?> ExistingInstanceResultAsync(int studentId, int testId)
    {
        var existing = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.StudentId == studentId && ti.WorksheetId == testId)
            .OrderBy(ti => ti.Status == WorksheetInstanceStatus.Started ? 1 : 0)
            .ThenByDescending(ti => ti.Id)
            .Select(ti => new { ti.Id, ti.Status, ti.StartTime, ti.MaxDurationSeconds, StudentUserId = ti.Student.UserId })
            .FirstOrDefaultAsync();

        if (existing == null)
            return null;

        // issue #396 review: the acting user is the student themself (audit UpdateUserId is not nulled).
        var status = await ExpireIfOverdueAsync(
            existing.Id, existing.Status, existing.StartTime, existing.MaxDurationSeconds, existing.StudentUserId);

        if (status != WorksheetInstanceStatus.Started)
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

    /// <summary>
    /// issue #396: <paramref name="status"/> Started ve süre (+ tolerans) dolmuşsa instance'ı koşullu UPDATE ile
    /// (<c>WHERE Status = Started</c>) Expired'a çeker ve sonuç durumunu döner. Koşullu yazma, SaveAnswer/EndTest'in
    /// aynı satırdaki koşullu UPDATE'leriyle (#367 kilidi) serileşir: yarışı kaybeden 0 satır görür ve kazananın
    /// yazdığı durumu okur. Süre sınırı yoksa / dolmamışsa <paramref name="status"/> aynen döner (yazma yok).
    /// Açık bir transaction varsa ona katılır (SaveAnswer). ExecuteUpdate audit hook'unu atladığından UpdateTime burada yazılır.
    /// </summary>
    private async Task<WorksheetInstanceStatus> ExpireIfOverdueAsync(
        int instanceId, WorksheetInstanceStatus status, DateTime startTime, int? maxDurationSeconds, int? actingUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (status != WorksheetInstanceStatus.Started || !TestTimeLimit.IsOver(startTime, maxDurationSeconds, now))
            return status;

        var endsAt = TestTimeLimit.EndsAt(startTime, maxDurationSeconds);
        var expired = await _context.TestInstances
            .Where(ti => ti.Id == instanceId && ti.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Expired)
                .SetProperty(ti => ti.EndTime, endsAt)
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, actingUserId), ct);

        if (expired == 1)
            return WorksheetInstanceStatus.Expired;

        // Yarışı başka bir istek kazandı (Completed ya da Expired yazdı) — onun sonucunu oku. IgnoreQueryFilters: satır
        // arada soft-delete edildiyse (öğrenci sıfırlama) de durumu okunabilsin; sorgu zaten Id ile tek satırdır.
        return await _context.TestInstances
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ti => ti.Id == instanceId)
            .Select(ti => ti.Status)
            .FirstAsync(ct);
    }

    /// <summary>
    /// UI sayacının toplam süresi: oturumun kopyası; yoksa (migration'ın dolduramadığı eski satır) worksheet'in bugünkü
    /// değeri. Sunucu süre kontrolü yalnız kopyaya bakar.
    /// </summary>
    private static int DisplayedLimit(WorksheetInstance instance) =>
        instance.MaxDurationSeconds ?? instance.Worksheet.MaxDurationSeconds;

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

        // issue #396: GET saftır — süresi dolmuş açık instance yanıtta Expired görünür (UI sonuç sayfasına yönlendirir),
        // veritabanına yazılmaz (kalıcı yazım: save/end/start ve ExpiredTestInstanceSweepJob).
        var now = DateTime.UtcNow;
        var status = TestTimeLimit.EffectiveStatus(instance.Status, instance.StartTime, instance.MaxDurationSeconds, now);

        var worksheetInstanceDto = new WorksheetInstanceDto
        {
            Id = instance.Id,
            TestName = instance.Worksheet.Name,
            Status = status,
            MaxDurationSeconds = DisplayedLimit(instance),
            RemainingSeconds = TestTimeLimit.RemainingSeconds(status, instance.StartTime, instance.MaxDurationSeconds, now),
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

        // issue #396: GET saftır — süresi dolmuş açık instance yanıtta Expired görünür (çözüm sayfası #383 Started
        // olmayanı sonuç sayfasına yönlendirir); kalıcı Expired yazımı save/end/start ve süpürücüdedir. Cevap kapısı
        // (SaveAnswer) aynı kuralı uyguladığından bu oturuma artık cevap yazılamaz.
        var now = DateTime.UtcNow;
        var status = TestTimeLimit.EffectiveStatus(testInstance.Status, testInstance.StartTime, testInstance.MaxDurationSeconds, now);

        // Doğru cevaplar yalnız bitmiş teste (Completed/Expired) gösterilir: Started iken gösterilseydi cevaplar
        // düzeltilebilirdi. Bitmiş oturuma cevap yazılamaz (#367/#396), bu yüzden sonuç sayfası Expired için de açık.
        if (includeCorrectAnswer && !WorksheetInstanceStatusRules.IsFinished(status))
        {
            return null;
        }

        var response = new WorksheetInstanceResultDto
        {
            Id = testInstance.Id,
            WorksheetId = testInstance.WorksheetId,
            TestName = testInstance.Worksheet.Name,
            Status = status,
            MaxDurationSeconds = DisplayedLimit(testInstance),
            RemainingSeconds = TestTimeLimit.RemainingSeconds(status, testInstance.StartTime, testInstance.MaxDurationSeconds, now),
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
                // Only to pick the message/reason (time-up vs. completed); the decision itself was the locked UPDATE.
                var finishedAs = await _context.TestInstances
                    .AsNoTracking()
                    .Where(ti => ti.Id == testInstanceQuestion.WorksheetInstanceId)
                    .Select(ti => new { ti.Status, ti.StartTime, ti.MaxDurationSeconds })
                    .FirstOrDefaultAsync(ct);
                return NotInProgressAnswer(timeExpired: finishedAs != null
                    && IsTimeUp(finishedAs.Status, finishedAs.StartTime, finishedAs.MaxDurationSeconds));
            }

            // issue #396: server-side time limit. Checked AFTER the status lock above, so StartTime/limit and the Expired
            // write below are decided while we hold the row — a concurrent EndTest waits and then sees Expired. The
            // Expired write is committed (not rolled back) so the instance is closed for every later request too; nothing
            // else (answer, revision, outbox) is written. The limit is the instance's own snapshot (copied at start-test).
            var timing = await _context.TestInstances
                .AsNoTracking()
                .Where(ti => ti.Id == testInstanceQuestion.WorksheetInstanceId)
                .Select(ti => new { ti.StartTime, ti.MaxDurationSeconds })
                .FirstAsync(ct);

            var status = await ExpireIfOverdueAsync(
                testInstanceQuestion.WorksheetInstanceId, WorksheetInstanceStatus.Started, timing.StartTime,
                timing.MaxDurationSeconds, user.Id, ct);
            if (status != WorksheetInstanceStatus.Started)
            {
                await transaction.CommitAsync(ct);
                _context.ChangeTracker.Clear();
                return NotInProgressAnswer(timeExpired: IsTimeUp(status, timing.StartTime, timing.MaxDurationSeconds));
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

    /// <param name="timeExpired">issue #396: oturum süre sınırıyla Expired — ayrı mesaj + <see cref="TestSessionRejectReasons.TimeExpired"/>.</param>
    private TestSessionResultDto NotInProgressAnswer(bool timeExpired) => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TestSessionErrorCodes.TestNotInProgress,
        Reason = timeExpired ? TestSessionRejectReasons.TimeExpired : null,
        Message = _localizer[timeExpired
            ? "worksheets.session.answerRejectedTimeUp"
            : "worksheets.session.answerRejectedNotInProgress"]
    };

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
        // issue #396: a Started instance past its time limit (+ tolerance) is closed as Expired, not Completed — the
        // conditional write in ExpireIfOverdueAsync races with SaveAnswer/another EndTest on the same row lock; the
        // loser falls through to the status read below (Expired → 409, Completed → idempotent success).
        var timing = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.Id == testInstanceId && ti.Student.UserId == userId)
            .Select(ti => new { ti.Status, ti.StartTime, ti.MaxDurationSeconds, ti.StudentId, ti.WorksheetId })
            .FirstOrDefaultAsync(ct);

        var currentStatus = timing?.Status;
        if (timing != null)
        {
            var current = await ExpireIfOverdueAsync(
                testInstanceId, timing.Status, timing.StartTime, timing.MaxDurationSeconds, userId, ct);
            if (current == WorksheetInstanceStatus.Expired)
                return NotInProgressEnd(timeExpired: IsTimeUp(current, timing.StartTime, timing.MaxDurationSeconds));
            currentStatus = current;
        }

        // issue #423: öğrencinin Active velisi varsa tamamlanma, veli event'leriyle AYNI transaction'da yazılır. Veli yoksa
        // (çoğunluk) yol değişmez: tek koşullu UPDATE, ekstra HTTP/outbox yok. Ad/sub çözümü transaction DIŞINDA, fail-soft.
        var parentPlan = timing != null && currentStatus == WorksheetInstanceStatus.Started
            ? await PlanParentNotificationsAsync(testInstanceId, userId, timing.StudentId, timing.WorksheetId, timing.StartTime, ct)
            : null;
        var now = DateTime.UtcNow; // issue #423 (m2): plandan SONRA alınır, EndTime/event zamanı gerçek tamamlanmaya yakın olur

        // ExecuteUpdate bypasses the SaveChanges audit hook, so UpdateTime/UpdateUserId are stamped here (the caller
        // is the instance's student — ownership is part of the WHERE).
        Task<int> ConditionalComplete() => _context.TestInstances
            .Where(ti => ti.Id == testInstanceId
                && ti.Student.UserId == userId
                && ti.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Completed)
                .SetProperty(ti => ti.EndTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

        var completed = 0;
        if (parentPlan == null)
        {
            completed = await ConditionalComplete();
        }
        else
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                completed = await ConditionalComplete();
                if (completed == 1)
                {
                    // UPDATE satır kilidini tutuyor ve Completed sonrası cevap yazımı reddediliyor → sayılar kesin.
                    // Security M1: Active veliler transaction İÇİNDE (FOR SHARE) yeniden okunur; plan ∩ şimdiki Active.
                    var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, parentPlan.StudentId, ct);
                    await AddParentCompletedEventsAsync(parentPlan, stillActive, testInstanceId, now, ct);
                    await _context.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
            });
        }

        if (completed == 1)
        {
            return new TestSessionResultDto
            {
                Success = true,
                Message = _localizer["worksheets.session.ended"]
            };
        }

        var final = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.Id == testInstanceId && ti.Student.UserId == userId)
            .Select(ti => new { ti.Status, ti.StartTime, ti.MaxDurationSeconds })
            .FirstOrDefaultAsync(ct);

        return final?.Status switch
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
            _ => NotInProgressEnd(timeExpired: IsTimeUp(final!.Status, final.StartTime, final.MaxDurationSeconds))
        };
    }

    private sealed record ParentCompletionPlan(
        int StudentId,
        int WorksheetId,
        string WorksheetName,
        string StudentDisplayName,
        IReadOnlyList<ActiveParentRecipient> Parents,
        IReadOnlyDictionary<int, NotificationUser> Users);

    /// <summary>
    /// issue #423: Started bir oturumun sahibi öğrencinin Active velileri varsa event planı; yoksa null (yol değişmez, ek HTTP/sorgu
    /// yok). Kapsam (security m1, V3/#421 ile aynı): oturum, öğrenciye GÖRÜNÜR bir atamanın penceresine (30 gün + instance penceresi,
    /// <see cref="AssignmentInstanceWindow"/>) girmelidir — serbest/kendi başına çözüm bildirilmez. Ad/sub çözümü fail-soft ve 1 sn
    /// ile sınırlı (auth-api yoksa boş; consumer BadgeService verisinden çözer).
    /// </summary>
    private async Task<ParentCompletionPlan?> PlanParentNotificationsAsync(
        int testInstanceId, int userId, int studentId, int worksheetId, DateTime instanceStartTime, CancellationToken ct)
    {
        var parents = await ParentNotificationSupport.ActiveParentsAsync(_context, studentId, ct);
        if (parents.Count == 0)
            return null;

        var student = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.GradeId, VerifiedSchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null })
            .FirstOrDefaultAsync(ct);
        if (student == null)
            return null;

        var planNow = DateTime.UtcNow;
        if (!await ParentAssignmentScope.IsWorksheetInScopeAsync(
                _context, studentId, student.GradeId, student.VerifiedSchoolId, worksheetId,
                instanceStartTime, WorksheetInstanceStatus.Started, planNow, ct))
            return null;

        var worksheetName = await _context.Worksheets.AsNoTracking()
            .Where(w => w.Id == worksheetId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var users = await ParentNotificationSupport.LookupAsync(
            _authApi, parents.Select(p => p.ParentUserId).Append(userId), logger: null, ct,
            timeoutOverride: TimeSpan.FromSeconds(1));
        return new ParentCompletionPlan(
            studentId, worksheetId, worksheetName,
            ParentNotificationSupport.Of(users, userId).DisplayName, parents, users);
    }

    /// <summary>
    /// issue #423: her Active veli için bir <see cref="ParentChildTestCompletedEvent"/> (tek alıcı / event); çağıranın
    /// transaction'ında, çağıranın SaveChanges'iyle yazılır. Puan, öğrencinin sonuç ekranıyla aynı formül.
    /// </summary>
    private async Task AddParentCompletedEventsAsync(
        ParentCompletionPlan plan, IReadOnlySet<int> stillActiveParentIds, int testInstanceId, DateTime completedAtUtc, CancellationToken ct)
    {
        // Security M1: yalnız hem planda hem transaction içi okumada Active olan veliler.
        var recipients = plan.Parents.Where(p => stillActiveParentIds.Contains(p.ParentId)).ToList();
        if (recipients.Count == 0)
            return;

        // Tek sorgu: toplam + doğru (öğrencinin sonuç ekranıyla aynı formül).
        var counts = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstanceId == testInstanceId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Correct = g.Count(q => q.SelectedAnswerId != null && q.WorksheetQuestion.Question.CorrectAnswerId == q.SelectedAnswerId)
            })
            .FirstOrDefaultAsync(ct);
        var total = counts?.Total ?? 0;
        var correct = counts?.Correct ?? 0;
        var score = WorksheetScoring.ScorePercent(correct, total);

        foreach (var parent in recipients)
        {
            _context.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<ParentChildTestCompletedEvent>(),
                Content = JsonSerializer.Serialize(new ParentChildTestCompletedEvent
                {
                    EventId = Guid.NewGuid(),
                    TestInstanceId = testInstanceId,
                    WorksheetId = plan.WorksheetId,
                    WorksheetName = plan.WorksheetName,
                    StudentId = plan.StudentId,
                    StudentDisplayName = plan.StudentDisplayName,
                    ParentId = parent.ParentId,
                    ParentUserId = parent.ParentUserId,
                    ParentKeycloakId = ParentNotificationSupport.Of(plan.Users, parent.ParentUserId).KeycloakId,
                    CorrectAnswers = correct,
                    TotalQuestions = total,
                    Score = score,
                    CompletedAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc)
                }),
                CreatedAt = completedAtUtc
            });
        }
    }

    /// <summary>
    /// issue #396: "süre doldu" yalnız oturum gerçekten süre sınırıyla kapandıysa — Expired VE süre (+ tolerans) geçmiş.
    /// Öğrenci sıfırlamasının (StudentResetJob) kapattığı oturum da Expired'dır ama süresi dolmamıştır → genel mesaj.
    /// </summary>
    private static bool IsTimeUp(WorksheetInstanceStatus status, DateTime startTime, int? maxDurationSeconds) =>
        status == WorksheetInstanceStatus.Expired && TestTimeLimit.IsOver(startTime, maxDurationSeconds, DateTime.UtcNow);

    private TestSessionResultDto NotInProgressEnd(bool timeExpired) => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TestSessionErrorCodes.TestNotInProgress,
        Reason = timeExpired ? TestSessionRejectReasons.TimeExpired : null,
        Message = _localizer[timeExpired ? "worksheets.session.timeUp" : "worksheets.session.notInProgress"]
    };
}
