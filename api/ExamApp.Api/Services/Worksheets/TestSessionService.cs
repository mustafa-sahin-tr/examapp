using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
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

    // issue #571: etkileşim tipine göre sunucu tarafı kontrolcü. DI verir; DI'sız birim testler yerleşik kontrolcüleri alır.
    private readonly IInteractionGraderRegistry _graders;

    public TestSessionService(AppDbContext context, IStringLocalizer<Messages>? localizer = null, IAuthApiClient? authApi = null,
        IInteractionGraderRegistry? graders = null)
    {
        _context = context;
        _authApi = authApi;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _graders = graders ?? InteractionGraderRegistry.Default;
    }

    public async Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(StudentProfileDto student, int pageNumber, int pageSize)
    {
        var query = await _context.TestInstances
            // issue #396: öğrencinin kendi geçmişi bitmiş oturumları listeler (Completed + Expired) — süresi dolan test de
            // sonuç sayfasına buradan ulaşılır. Puan, cevaplananlar üzerinden aynı formülle hesaplanır.
            .Where(wi => wi.StudentId == student.Id
                && (wi.Status == WorksheetInstanceStatus.Completed || wi.Status == WorksheetInstanceStatus.Expired)
                // issue #571: etkinlikler puansız — skor listesine girmez.
                && wi.Worksheet.Kind != WorksheetKind.Activity)
            .Select(wi => new
            {
                wi.Id,
                wi.Worksheet.Name,
                wi.Worksheet.ImageUrl,
                wi.EndTime,
                wi.StartTime,
                TotalQuestions = wi.WorksheetInstanceQuestions.Count(),

                // issue #571: WorksheetScoring hibrit kuralı (MCQ aynı; etkileşimli cevaplar da sayılır).
                // Yanlış = toplam − doğru − boş: sorusu silinmiş boş olmayan satır da yanlış sayılır (WorksheetScoring dokümanı).
                CorrectAnswers = wi.WorksheetInstanceQuestions.AsQueryable().Count(WorksheetScoring.IsCorrectRow),
                BlankAnswers = wi.WorksheetInstanceQuestions.AsQueryable().Count(WorksheetScoring.IsBlankRow)
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
            WrongAnswers = wi.TotalQuestions - wi.CorrectAnswers - wi.BlankAnswers,
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

        // Teste ait soruları TestQuestion tablosundan çekiyoruz (kilit dışında okunur). Include'lar korunur: soft-delete
        // filtreli soru gezinmesi silinmiş sorunun satırını önceki davranıştaki gibi dışarıda bırakır.
        var testQuestionIds = (await _context.TestQuestions
            .AsNoTracking()
            .Where(tq => tq.TestId == testId)
            .Include(tq => tq.Question)
                .ThenInclude(q => q.QuestionSubTopics)
            .OrderBy(tq => tq.Order)
            .ThenBy(tq => tq.Id) // issue #309: eşit Order'da kararlı sıra (WorksheetQuestionNumbering ile aynı)
            .ToListAsync())
            .Select(tq => tq.Id)
            .ToList();

        // issue #573: oturum INSERT'i worksheet tür kilidinin (WorksheetKindLock) altında — tür değişimiyle (WorksheetAuthoringService)
        // serileşir: değişim önce commit edildiyse süre kopyası yeni değerden okunur (etkinlik → 0); oturum önce commit edildiyse
        // değişim kilidin altında oturumu görüp 409 WorksheetKindLocked döner. SaveAnswer ile aynı yeniden deneme deseni.
        var strategy = _context.Database.CreateExecutionStrategy();
        var created = await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.AcquireWorksheetKindLockAsync(testId);

            // issue #396: süre sınırının kopyası — öğretmen worksheet süresini sonradan değiştirse de bu oturum etkilenmez.
            // Kilidin altında yeniden okunur (tür değişimi süreyi 0'a çekmiş olabilir); worksheet arada silindiyse ilk okuma.
            var maxDurationSeconds = await _context.Worksheets.AsNoTracking()
                .Where(w => w.Id == testId)
                .Select(w => (int?)w.MaxDurationSeconds)
                .FirstOrDefaultAsync() ?? worksheet.MaxDurationSeconds;

            var instance = new WorksheetInstance
            {
                WorksheetId = testId,
                StudentId = student.Id,
                Status = WorksheetInstanceStatus.Started,
                WorksheetInstanceQuestions = testQuestionIds.Select(id => new WorksheetInstanceQuestion
                {
                    WorksheetQuestionId = id,
                    IsCorrect = false,
                    TimeTaken = 0
                }).ToList(),
                StartTime = DateTime.UtcNow,
                MaxDurationSeconds = maxDurationSeconds
            };

            _context.TestInstances.Add(instance);
            try
            {
                await _context.SaveChangesAsync(); // burada audit çalışır
            }
            catch (DbUpdateException ex) when (IsDuplicateLiveInstanceViolation(ex))
            {
                // issue #367: eşzamanlı ikinci start-test (çift tık / iki sekme) IX_TestInstances_StudentId_WorksheetId'ye
                // düştü: bizimkini bırak, kazananın instance'ını döndür (DailyQuestionSetService ile aynı desen).
                await tx.RollbackAsync();
                _context.ChangeTracker.Clear();
                return (WorksheetInstance?)null;
            }

            await tx.CommitAsync();
            return instance;
        });

        if (created == null)
        {
            return await ExistingInstanceResultAsync(student.Id, testId)
                ?? throw new InvalidOperationException("Test instance unique violation but no existing instance found.");
        }

        return new TestStartResultDto
        {
            Success = true,
            InstanceId = created.Id,
            StartTime = created.StartTime
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
                            ImageUrl = tiq.WorksheetQuestion.Question.Passage.ImageUrl,
                            Kind = tiq.WorksheetQuestion.Question.Passage.Kind, // issue #573
                            Instruction = tiq.WorksheetQuestion.Question.Passage.Instruction
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

        // issue #573: etkinlik oynatıcısı yeniden yüklenince kontrol durumu geri gelsin diye kontrol sayaçları ve (aşağıda)
        // satır bazlı son kontrol sonucu döner. Test oturumlarında bu alanlar null ve maruz kalma kuralları değişmez.
        var isActivity = testInstance.Worksheet.Kind == WorksheetKind.Activity;

        var response = new WorksheetInstanceResultDto
        {
            Id = testInstance.Id,
            WorksheetId = testInstance.WorksheetId,
            TestName = testInstance.Worksheet.Name,
            Status = status,
            MaxDurationSeconds = DisplayedLimit(testInstance),
            RemainingSeconds = TestTimeLimit.RemainingSeconds(status, testInstance.StartTime, testInstance.MaxDurationSeconds, now),
            IsPracticeTest = testInstance.Worksheet.IsPracticeTest,
            Kind = testInstance.Worksheet.Kind,
            WorksheetDescription = testInstance.Worksheet.Description,
            CheckCount = isActivity ? testInstance.CheckCount : null,
            FirstCheckedAt = isActivity ? testInstance.FirstCheckedAt : null,
            SolutionsRevealedAt = isActivity ? testInstance.SolutionsRevealedAt : null,
            FirstCheckCorrectCount = isActivity ? testInstance.FirstCheckCorrectCount : null,
            FirstCheckTotalCount = isActivity ? testInstance.FirstCheckTotalCount : null,
            FirstCompletedAt = isActivity ? testInstance.FirstCompletedAt : null, // issue #609
            TestInstanceQuestions = OrderedForDisplay(testInstance.WorksheetInstanceQuestions).Select(tiq =>
            {
                var questionEntity = tiq.WorksheetQuestion.Question;
                // issue #570: plan çözümsüz; etkileşim çözümü CorrectAnswerId ile aynı kapıdan (bitmiş test) ya da örnek soruda.
                var (interactionPlan, interactionSolution) = InteractionPlanSolution.ForOutput(
                    questionEntity.InteractionPlan, questionEntity.InteractionSolution,
                    includeSolution: questionEntity.IsExample || includeCorrectAnswer);
                var questionDto = new QuestionDto
                {
                    Id = questionEntity.Id,
                    Text = questionEntity?.Text ?? string.Empty,
                    SubText = questionEntity.SubText,
                    ImageUrl = questionEntity.ImageUrl,
                    IsExample = questionEntity.IsExample,
                    InteractionType = questionEntity.InteractionType,
                    InteractionPlan = interactionPlan,
                    InteractionSolution = interactionSolution,
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
                        Height = questionEntity.Passage?.Height,
                        Kind = questionEntity.Passage?.Kind ?? PassageKind.Reading, // issue #573
                        Instruction = questionEntity.Passage?.Instruction
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


                // issue #571: satır bazlı sonuç yalnız etkileşimli (MCQ dışı) cevapta ve çözümle aynı kapıdan (bitmiş test).
                // issue #573: etkinlikte ayrıca son kontrol sonucu — yalnız satır kontrol edildikten sonra DEĞİŞMEDİYSE
                // (CheckedAnswerRevision == AnswerRevision); kontrol ucunun zaten döndürdüğü bilgi (MCQ dahil), çözüm değil.
                var exposeGrade = (includeCorrectAnswer && !IsMcq(questionEntity.InteractionType))
                    || (isActivity && tiq.CheckedAnswerRevision.HasValue && tiq.CheckedAnswerRevision == tiq.AnswerRevision);
                return new WorksheetInstanceQuestionDto
                {
                    Id = tiq.Id,
                    Order = tiq.WorksheetQuestion.Order,
                    Question = questionDto,
                    SelectedAnswerId = tiq.SelectedAnswerId,
                    AnswerPayload = tiq.AnswerPayload,
                    TimeTaken = tiq.TimeTaken,
                    IsCorrect = exposeGrade ? tiq.IsCorrect : null,
                    CorrectCount = exposeGrade ? tiq.CorrectCount : null,
                    TotalCount = exposeGrade ? tiq.TotalCount : null,
                    Feedback = exposeGrade ? tiq.Feedback : null
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
    private static bool IsMcq(string? interactionType)
        => string.IsNullOrWhiteSpace(interactionType)
            || interactionType.Trim().Equals(McqGrader.Type, StringComparison.OrdinalIgnoreCase);

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

            // issue #571: the payload is validated by the question type's grader BEFORE the transaction, the status lock
            // and the AnswerRevision increment — an invalid payload (unknown zone/card id, duplicate, oversized, malformed)
            // is rejected (400) and nothing is written.
            // review #571: global 16 KB cap for every type; MCQ stores no payload (Normalize → null); a non-MCQ question
            // stores no option id; an option id must belong to this question (else 400 instead of an FK 500); an empty
            // drag-drop payload (no placements) is stored as null (blank, not wrong).
            var grader = _graders.Resolve(question.InteractionType);
            var prepared = await PrepareAnswerAsync(question, grader,
                dto.SelectedAnswerId > 0 ? dto.SelectedAnswerId : null, dto.AnswerPayload, ct);
            if (prepared == null)
                return InvalidPayloadAnswer();
            var (selectedAnswerId, payload) = prepared.Value;

            // review #571 (blocker): an Activity (Kind = Activity) is pointless (#569) — its answer is stored and graded but
            // NO AnswerSubmittedEvent is written (otherwise check → reveal solutions → save-answer would award points).
            // Read without the Worksheet query filter so a worksheet soft-deleted mid-test still resolves (default Test).
            var isActivity = await WorksheetKindOfInstanceAsync(testInstanceQuestion.WorksheetInstanceId, ct) == WorksheetKind.Activity;

            // issue #279 review: see the AnswerRevision comment below for why the transaction exists at all.
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            var rejected = await LockStartedInstanceAsync(testInstanceQuestion.WorksheetInstanceId, user.Id, transaction, ct);
            if (rejected != null)
                return rejected;

            // Store MCQ selection and/or structured answer payload
            testInstanceQuestion.SelectedAnswerId = selectedAnswerId;
            testInstanceQuestion.AnswerPayload = payload;
            testInstanceQuestion.TimeTaken = dto.TimeTaken;

            // issue #571: server-side grading by interaction type (MCQ: unchanged rule, selected == correct; dragDropLabeling:
            // per drop zone). IsCorrect = all rows correct; no partial points (the event still carries only IsCorrect).
            var isCorrect = ApplyGrade(testInstanceQuestion, question, grader);

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

            if (isActivity)
            {
                // review #571: activity — answer, grade fields and revision only; no outbox row (completion chain is #572).
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new TestSessionResultDto
                {
                    Success = true,
                    Message = _localizer["worksheets.session.answerSaved"]
                };
            }

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
                SelectedAnswerId = selectedAnswerId
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
    /// issue #367/#396 (extracted unchanged from <see cref="SaveAnswer"/> for issue #571, shared with the activity check): the
    /// FIRST statement of an open write transaction. Takes the instance row lock with a conditional no-op UPDATE (Started only)
    /// and applies the server-side time limit. null = still Started, the caller holds the row lock and may write; otherwise the
    /// transaction is already rolled back (not Started) or committed (Expired write) and the 409 result is returned.
    /// </summary>
    private async Task<TestSessionResultDto?> LockStartedInstanceAsync(
        int instanceId, int actingUserId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
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
            .Where(ti => ti.Id == instanceId
                && ti.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s.SetProperty(ti => ti.Status, ti => ti.Status), ct);

        if (stillInProgress == 0)
        {
            await transaction.RollbackAsync(ct);
            _context.ChangeTracker.Clear();
            // Only to pick the message/reason (time-up vs. completed); the decision itself was the locked UPDATE.
            var finishedAs = await _context.TestInstances
                .AsNoTracking()
                .Where(ti => ti.Id == instanceId)
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
            .Where(ti => ti.Id == instanceId)
            .Select(ti => new { ti.StartTime, ti.MaxDurationSeconds })
            .FirstAsync(ct);

        var status = await ExpireIfOverdueAsync(
            instanceId, WorksheetInstanceStatus.Started, timing.StartTime,
            timing.MaxDurationSeconds, actingUserId, ct);
        if (status != WorksheetInstanceStatus.Started)
        {
            await transaction.CommitAsync(ct);
            _context.ChangeTracker.Clear();
            return NotInProgressAnswer(timeExpired: IsTimeUp(status, timing.StartTime, timing.MaxDurationSeconds));
        }

        return null;
    }

    /// <summary>
    /// issue #571: satırı tipin kontrolcüsüyle puanlar ve sonucu satıra yazar (<c>IsCorrect</c>, <c>CorrectCount</c>,
    /// <c>TotalCount</c>, <c>GradedBy = Auto</c>, <c>Feedback</c>). Satırın <c>SelectedAnswerId</c>/<c>AnswerPayload</c>'u önceden
    /// set edilmiş olmalı. Çözüm: <c>Question.InteractionSolution</c>, yoksa (taşınmamış eski satır) plandan ayrılan.
    /// </summary>
    private static InteractionGradeResult Grade(WorksheetInstanceQuestion row, Question question, IInteractionGrader grader)
    {
        var (_, solution) = InteractionPlanSolution.ForOutput(question.InteractionPlan, question.InteractionSolution, includeSolution: true);
        return grader.Grade(new InteractionGradeInput(
            question.InteractionPlan, solution, row.AnswerPayload, row.SelectedAnswerId, question.CorrectAnswerId));
    }

    private static bool ApplyGrade(WorksheetInstanceQuestion row, Question question, IInteractionGrader grader)
    {
        var result = Grade(row, question, grader);
        row.IsCorrect = result.IsCorrect;
        row.CorrectCount = result.CorrectCount;
        row.TotalCount = result.TotalCount;
        row.GradedBy = AnswerGradedBy.Auto;
        row.Feedback = result.Feedback;
        return result.IsCorrect;
    }

    /// <summary>issue #573: etkinlik kontrol edilmeden bitirilemez → 409; oturum Started kalır.</summary>
    private TestSessionResultDto ActivityEndCheckRequired() => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TestSessionErrorCodes.ActivityCheckRequired,
        Message = _localizer["worksheets.session.activityEndCheckRequired"]
    };

    /// <summary>issue #571: geçersiz etkileşim cevabı → 400; hiçbir şey yazılmadı.</summary>
    private TestSessionResultDto InvalidPayloadAnswer() => new()
    {
        Success = false,
        InvalidInput = true,
        ErrorCode = TestSessionErrorCodes.InvalidAnswerPayload,
        Message = _localizer["worksheets.session.invalidAnswerPayload"]
    };

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
    public Task<TestSessionResultDto> EndTest(int testInstanceId, int userId, CancellationToken ct = default)
        => EndTest(testInstanceId, userId, request: null, clientId: null, ct);

    /// <inheritdoc />
    public async Task<TestSessionResultDto> EndTest(
        int testInstanceId, int userId, EndTestRequestDto? request, string? clientId, CancellationToken ct = default)
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
        // issue #571: etkinlik (Kind = Activity) puansız — veliye test sonucu bildirimi gitmez (tamamlanma zinciri #572).
        // Kind ayrı okunur (review #571): timing projeksiyonu Worksheet'e JOIN etmemeli — sınav sırasında soft-delete edilen
        // worksheet'te timing null olur ve #396 süre kontrolü atlanırdı.
        var openKind = timing != null && currentStatus == WorksheetInstanceStatus.Started
            ? await WorksheetKindOfInstanceAsync(testInstanceId, ct)
            : (WorksheetKind?)null;

        // issue #573: etkinlik en az bir "Cevapları kontrol et"ten sonra bitirilebilir (boş maddeler engellemez). Buradaki okuma
        // yalnız temiz hata mesajı için ön kontroldür; asıl kural aşağıdaki koşullu UPDATE'in WHERE'inde (tür + CheckCount aynı
        // satır kilidinin altında değerlendirilir). Tür, oturumu olan sayfada değişemez (WorksheetKindLock).
        if (openKind == WorksheetKind.Activity)
        {
            var checkCount = await _context.TestInstances.AsNoTracking()
                .Where(ti => ti.Id == testInstanceId)
                .Select(ti => ti.CheckCount)
                .FirstOrDefaultAsync(ct);
            if (checkCount < 1)
                return ActivityEndCheckRequired();
        }

        var parentPlan = openKind is { } kind && kind != WorksheetKind.Activity
            ? await PlanParentNotificationsAsync(testInstanceId, userId, timing!.StudentId, timing.WorksheetId, timing.StartTime, ct)
            : null;
        // issue #572: etkinlik tamamlanınca tek ActivityCompletedEvent — atama / ders / konu transaction DIŞINDA okunur (salt okuma).
        var activityPlan = openKind == WorksheetKind.Activity
            ? await PlanActivityCompletionAsync(timing!.StudentId, timing.WorksheetId, timing.StartTime, ct)
            : null;
        var now = DateTime.UtcNow; // issue #423 (m2): plandan SONRA alınır, EndTime/event zamanı gerçek tamamlanmaya yakın olur

        // ExecuteUpdate bypasses the SaveChanges audit hook, so UpdateTime/UpdateUserId are stamped here (the caller
        // is the instance's student — ownership is part of the WHERE).
        Task<int> ConditionalComplete() => _context.TestInstances
            .Where(ti => ti.Id == testInstanceId
                && ti.Student.UserId == userId
                && ti.Status == WorksheetInstanceStatus.Started
                // issue #573: etkinlik ancak en az bir kontrolden sonra tamamlanır (Worksheet query filter'ı olmadan — sınav
                // sırasında silinen worksheet de çözülür, WorksheetKindOfInstanceAsync ile aynı).
                && (ti.CheckCount > 0 || !_context.Worksheets.IgnoreQueryFilters()
                    .Any(w => w.Id == ti.WorksheetId && w.Kind == WorksheetKind.Activity)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Completed)
                .SetProperty(ti => ti.EndTime, (DateTime?)now)
                // issue #609: ilk tamamlanma anı bir kez yazılır; yeniden çözülen etkinliğin tekrar tamamlanması değiştirmez.
                .SetProperty(ti => ti.FirstCompletedAt, ti => ti.FirstCompletedAt ?? (DateTime?)now)
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

        var completed = 0;
        if (parentPlan == null && activityPlan == null)
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
                if (completed == 1 && parentPlan != null)
                {
                    // UPDATE satır kilidini tutuyor ve Completed sonrası cevap yazımı reddediliyor → sayılar kesin.
                    // Security M1: Active veliler transaction İÇİNDE (FOR SHARE) yeniden okunur; plan ∩ şimdiki Active.
                    var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, parentPlan.StudentId, ct);
                    await AddParentCompletedEventsAsync(parentPlan, stillActive, testInstanceId, now, ct);
                    await _context.SaveChangesAsync(ct);
                }
                else if (completed == 1 && activityPlan != null)
                {
                    // issue #572: event yalnız bu çağrı Started → Completed yaptıysa (1 satır) ve AYNI transaction'da yazılır; ikinci
                    // EndTest 0 satır günceller → ikinci event yok. Satır kilidi tutulduğu için madde sayısı / süreler kesin.
                    await AddActivityCompletedEventAsync(
                        activityPlan, testInstanceId, userId, clientId, request?.ActiveSeconds, timing!.StartTime, now, ct);
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
            // issue #573: hâlâ Started ama UPDATE 0 satır = etkinlik kontrol edilmemiş (WHERE koruması).
            WorksheetInstanceStatus.Started => ActivityEndCheckRequired(),
            _ => NotInProgressEnd(timeExpired: IsTimeUp(final!.Status, final.StartTime, final.MaxDurationSeconds))
        };
    }

    /// <summary>
    /// issue #571: etkinlik "Cevapları kontrol et". Yalnız oturumun sahibi öğrenci (yoksa NotFound) ve yalnız etkinlikte
    /// (<c>Worksheet.Kind = Activity</c>, yoksa 409 <see cref="TestSessionErrorCodes.NotAnActivity"/>). Önce tüm maddeler
    /// doğrulanır (biri bile geçersizse 400, hiçbir şey yazılmaz); sonra tek transaction'da: oturum kilidi (Started değilse
    /// 409, <see cref="SaveAnswer"/> ile aynı kural), cevaplar + kontrol sonuçları, <c>AnswerRevision</c> artışı,
    /// <c>CheckCount</c>+1, <c>FirstCheckedAt</c> (ilk kez). Outbox event'i YAZILMAZ (etkinlik tamamlama zinciri #572).
    /// Yanıt satır bazlı doğru/yanlış döner; çözümü / doğru şıkkı döndürmez.
    /// </summary>
    public async Task<ActivityCheckResultDto> CheckActivityAsync(
        int instanceId, ActivityCheckRequestDto request, int userId, CancellationToken ct = default)
    {
        var answers = request?.Answers;
        if (answers == null || answers.Count == 0 || answers.Count > ActivityCheckRequestDto.MaxAnswers
            || answers.Any(a => a == null || a.InstanceQuestionId <= 0)
            || answers.Select(a => a.InstanceQuestionId).Distinct().Count() != answers.Count)
            return InvalidCheckRequest<ActivityCheckResultDto>();

        // Sahiplik + tür burada hızlı ön kontrol (hata sırası: NotFound → NotAnActivity → TestNotInProgress). Tür aşağıda oturum
        // kilidinin İÇİNDE yeniden okunur. #573: Kind yalnız hiç oturumu olmayan sayfada, WorksheetKindLock altında değişir
        // (start-test'in INSERT'iyle aynı kilit) — bu oturum var olduğuna göre tür artık değişemez; iç okuma ek güvencedir.
        var owned = await _context.TestInstances.AsNoTracking()
            .AnyAsync(ti => ti.Id == instanceId && ti.Student.UserId == userId, ct);
        if (!owned)
            return InstanceNotFound<ActivityCheckResultDto>();
        if (await WorksheetKindOfInstanceAsync(instanceId, ct) != WorksheetKind.Activity)
            return NotAnActivity<ActivityCheckResultDto>();

        var ids = answers.Select(a => a.InstanceQuestionId).ToList();

        // SaveAnswer ile aynı yeniden deneme deseni: tüm birim (yükleme, kilit, yazma, commit) strateji delegesinin içinde.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async _ =>
        {
            _context.ChangeTracker.Clear();

            var rows = await _context.TestInstanceQuestions
                .Include(t => t.WorksheetQuestion)
                    .ThenInclude(wq => wq.Question)
                .Where(t => t.WorksheetInstanceId == instanceId && ids.Contains(t.Id))
                .ToListAsync(ct);
            if (rows.Count != ids.Count || rows.Any(r => r.WorksheetQuestion?.Question == null))
                return InvalidCheckRequest<ActivityCheckResultDto>();

            var byId = rows.ToDictionary(r => r.Id);
            var plan = new List<(WorksheetInstanceQuestion Row, Question Question, IInteractionGrader Grader, int? Selected, string? Payload)>();
            foreach (var answer in answers)
            {
                var row = byId[answer.InstanceQuestionId];
                var question = row.WorksheetQuestion.Question;
                var grader = _graders.Resolve(question.InteractionType);
                // save-answer ile aynı hazırlık: 16 KB sınırı, doğrulama, normalize, şık sahipliği (geçersiz → 400, yazma yok).
                var prepared = await PrepareAnswerAsync(question, grader,
                    answer.SelectedAnswerId > 0 ? answer.SelectedAnswerId : null, answer.AnswerPayload, ct);
                if (prepared == null)
                    return As<ActivityCheckResultDto>(InvalidPayloadAnswer());
                plan.Add((row, question, grader, prepared.Value.SelectedAnswerId, prepared.Value.Payload));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            var rejected = await LockStartedInstanceAsync(instanceId, userId, transaction, ct);
            if (rejected != null)
                return As<ActivityCheckResultDto>(rejected);

            // issue #573 (review): tür oturum kilidinin altında yeniden okunur.
            if (await WorksheetKindOfInstanceAsync(instanceId, ct) != WorksheetKind.Activity)
            {
                await transaction.RollbackAsync(ct);
                _context.ChangeTracker.Clear();
                return NotAnActivity<ActivityCheckResultDto>();
            }

            var results = new List<ActivityCheckQuestionResultDto>(plan.Count);
            foreach (var (row, question, grader, selected, payload) in plan)
            {
                row.SelectedAnswerId = selected;
                row.AnswerPayload = payload;
                ApplyGrade(row, question, grader);
                results.Add(new ActivityCheckQuestionResultDto
                {
                    InstanceQuestionId = row.Id,
                    IsCorrect = row.IsCorrect,
                    CorrectCount = row.CorrectCount,
                    TotalCount = row.TotalCount,
                    Feedback = row.Feedback
                });
            }

            // AnswerRevision: SaveAnswer'daki gibi atomik artış (her cevap yazımı revizyonu ilerletir). İzlenen satırlarda
            // AnswerRevision Modified değil, SaveChanges artışı ezmez.
            // issue #573: CheckedAnswerRevision = yeni revizyon (aynı UPDATE, sağ taraf eski değer) — yeniden yüklemede bu satırın
            // kontrol sonucu yalnız sonradan save-answer ile değişmediyse döner.
            await _context.TestInstanceQuestions
                .Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.AnswerRevision, x => x.AnswerRevision + 1)
                    .SetProperty(x => x.CheckedAnswerRevision, x => (int?)(x.AnswerRevision + 1)), ct);

            var now = DateTime.UtcNow;
            var checkCorrect = results.Sum(r => r.CorrectCount);
            var checkTotal = results.Sum(r => r.TotalCount);
            // issue #573: ilk kontrol özeti (öğretmen takibi) yalnız ilk kontrolde, aynı UPDATE'te (SET sağ tarafları eski satırı
            // görür: FirstCheckedAt == null yalnız ilk kontrolde doğru). Oturum satırı kilitli olduğundan iki ilk kontrol yarışamaz.
            await _context.TestInstances
                .Where(ti => ti.Id == instanceId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(ti => ti.CheckCount, ti => ti.CheckCount + 1)
                    .SetProperty(ti => ti.FirstCheckCorrectCount, ti => ti.FirstCheckedAt == null ? (int?)checkCorrect : ti.FirstCheckCorrectCount)
                    .SetProperty(ti => ti.FirstCheckTotalCount, ti => ti.FirstCheckedAt == null ? (int?)checkTotal : ti.FirstCheckTotalCount)
                    .SetProperty(ti => ti.FirstCheckedAt, ti => ti.FirstCheckedAt ?? (DateTime?)now)
                    .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                    .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new ActivityCheckResultDto
            {
                Success = true,
                Message = _localizer["worksheets.session.activityChecked"],
                CorrectCount = checkCorrect,
                TotalCount = checkTotal,
                Questions = results
            };
        }, ct);
    }

    /// <summary>
    /// issue #571: etkinlik "Doğru cevapları göster". Yalnız sahibi (yoksa NotFound), yalnız etkinlik (409
    /// <see cref="TestSessionErrorCodes.NotAnActivity"/>) ve en az bir kontrolden sonra (<c>CheckCount == 0</c> → 409
    /// <see cref="TestSessionErrorCodes.ActivityCheckRequired"/>). İlk açılışta <c>SolutionsRevealedAt</c> yazılır; sonrasında
    /// yeniden kontrol serbesttir. MCQ'da doğru şık, etkileşimli soruda çözüm döner.
    /// <para>
    /// Kabul edilmiş ürün riski (issue #573 security review D1): bir öğretmen başka sahibin damgalı sorusunu (#472
    /// <c>CanLink</c>) kendi etkinliğine bağlayıp kendine atayarak bir kontrolden sonra o sorunun <c>interactionSolution</c>'ını
    /// görebilir. Bu, önceden var olan "bitmiş testin sonuç sayfası" yolundaki maruziyetle aynıdır (orada da bağlanan sorunun
    /// çözümü bitirince görünür); ek bir kapı eklenmedi. Takip: #581 / #584.
    /// </para>
    /// </summary>
    public async Task<ActivitySolutionsResultDto> GetActivitySolutionsAsync(int instanceId, int userId, CancellationToken ct = default)
    {
        var instance = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == instanceId && ti.Student.UserId == userId)
            .Select(ti => new { ti.CheckCount })
            .FirstOrDefaultAsync(ct);
        if (instance == null)
            return InstanceNotFound<ActivitySolutionsResultDto>();
        if (await WorksheetKindOfInstanceAsync(instanceId, ct) != WorksheetKind.Activity)
            return NotAnActivity<ActivitySolutionsResultDto>();
        if (instance.CheckCount == 0)
        {
            return new ActivitySolutionsResultDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = TestSessionErrorCodes.ActivityCheckRequired,
                Message = _localizer["worksheets.session.activityCheckRequired"]
            };
        }

        var now = DateTime.UtcNow;
        await _context.TestInstances
            .Where(ti => ti.Id == instanceId && ti.SolutionsRevealedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(ti => ti.SolutionsRevealedAt, (DateTime?)now)
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

        var rows = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(t => t.WorksheetInstanceId == instanceId)
            .Select(t => new
            {
                t.Id,
                t.WorksheetQuestion.Order,
                WorksheetQuestionId = t.WorksheetQuestion.Id,
                t.WorksheetQuestion.QuestionId,
                t.WorksheetQuestion.Question.InteractionType,
                t.WorksheetQuestion.Question.CorrectAnswerId,
                t.WorksheetQuestion.Question.InteractionPlan,
                t.WorksheetQuestion.Question.InteractionSolution
            })
            .ToListAsync(ct);

        return new ActivitySolutionsResultDto
        {
            Success = true,
            Questions = rows
                .OrderBy(r => r.Order).ThenBy(r => r.WorksheetQuestionId) // issue #309 görüntü sırası
                .Select(r =>
                {
                    var mcq = IsMcq(r.InteractionType);
                    return new ActivitySolutionDto
                    {
                        InstanceQuestionId = r.Id,
                        QuestionId = r.QuestionId,
                        CorrectAnswerId = mcq ? r.CorrectAnswerId : null,
                        InteractionSolution = mcq
                            ? null
                            : InteractionPlanSolution.ForOutput(r.InteractionPlan, r.InteractionSolution, includeSolution: true).Solution
                    };
                })
                .ToList()
        };
    }

    /// <summary>
    /// issue #609: tamamlanmış etkinliği "Yeniden çöz". Yeni oturum AÇILMAZ (öğrenci + worksheet başına tek canlı oturum,
    /// #367 unique index'i) — aynı oturum sıfırlanır. Yalnız oturumun sahibi (yoksa NotFound), yalnız etkinlik (409
    /// <see cref="TestSessionErrorCodes.NotAnActivity"/>; test davranışı değişmez) ve yalnız Completed oturum (409
    /// <see cref="TestSessionErrorCodes.ActivityNotCompleted"/>: hiç tamamlanmamış Started ya da Expired). Oturum zaten yeniden
    /// çözülüyorsa (Started + <c>FirstCompletedAt</c> dolu) idempotent başarı, hiçbir şey sıfırlanmaz (<c>AlreadyRetaking</c>).
    /// <para>
    /// Tek transaction: önce koşullu UPDATE (<c>WHERE Status = Completed</c>) — satır kilidini alır, EndTest/SaveAnswer/kontrol
    /// ile aynı kilitte serileşir; iki eşzamanlı retake'ten yalnız biri 1 satır görür, kaybeden hiçbir şeyi sıfırlamaz (kazananın
    /// açtığı oturumda yazılan cevaplar silinmez). Sonra maddelerin cevap/kontrol alanları temizlenir (satırlar kalır,
    /// <c>AnswerRevision</c> ileri gider). Oturumda: Status = Started, EndTime / SolutionsRevealedAt = null, CheckCount = 0.
    /// KORUNAN: <c>FirstCheckCorrectCount/TotalCount/FirstCheckedAt</c> (öğretmenin gördüğü ilk sonuç; kontrol yalnız
    /// <c>FirstCheckedAt == null</c> iken yazar), <c>FirstCompletedAt</c>, <c>ActiveSeconds</c> (yeniden tamamlamada birikir) ve
    /// <c>StartTime</c> (atama penceresi <see cref="AssignmentInstanceWindow"/> buna bakar; sıfırlansaydı teslim tarihinden
    /// sonra yeniden çözülen etkinlik öğretmen takibinden düşerdi). Denemenin başlangıcı ayrıca <c>AttemptStartedAt</c>'e yazılır
    /// (etkin süre penceresi), <c>RetakeCount</c> +1. Erişim start-test kuralıyla (403 AccessDenied); silinmiş etkinlik 404. Etkinlik süresiz olduğundan (#573) süre kopyası 0'a çekilir —
    /// eski bir oturumun kopyası kalmışsa eski StartTime yüzünden yeniden açılan oturum hemen Expired olmasın.
    /// </para>
    /// Yeniden tamamlama EndTest'te yine bir <see cref="ActivityCompletedEvent"/> yazar (aynı WorksheetInstanceId); BadgeService
    /// (UserId, WorksheetInstanceId) ile tekilleştirdiği için rozet sayaçları şişmez. Puan yok (etkinlik puansız, #571).
    /// </summary>
    public async Task<ActivityRetakeResultDto> RetakeActivityAsync(int instanceId, int userId, CancellationToken ct = default)
    {
        var instance = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == instanceId && ti.Student.UserId == userId)
            .Select(ti => new
            {
                ti.Status, ti.FirstCompletedAt, ti.WorksheetId, ti.StudentId,
                ti.Student.GradeId, VerifiedSchoolId = ti.Student.SchoolVerifiedAt != null ? ti.Student.SchoolId : null
            })
            .FirstOrDefaultAsync(ct);
        if (instance == null)
            return InstanceNotFound<ActivityRetakeResultDto>();
        if (await WorksheetKindOfInstanceAsync(instanceId, ct) != WorksheetKind.Activity)
            return NotAnActivity<ActivityRetakeResultDto>();

        // review M2: yeniden çözme yeni bir çözüm başlatmaktır → start-test ile AYNI erişim kararı (atama / sınıf + görünürlük).
        // Silinmiş etkinlik (Worksheet query filter'ı) yeniden açılmaz.
        var worksheet = await _context.Worksheets.AsNoTracking()
            .Where(w => w.Id == instance.WorksheetId)
            .Select(w => new { w.GradeId, w.StudentVisibility })
            .FirstOrDefaultAsync(ct);
        if (worksheet == null)
            return RetakeWorksheetNotFound();
        var hasActiveAssignment = await _context.ActiveAssignmentsFor(instance.StudentId, instance.GradeId, instance.VerifiedSchoolId, DateTime.UtcNow)
            .AnyAsync(a => a.WorksheetId == instance.WorksheetId, ct);
        var isGradeMatch = instance.GradeId.HasValue && worksheet.GradeId == instance.GradeId.Value;
        if (!WorksheetAccess.CanStudentStartTest(hasActiveAssignment, isGradeMatch, worksheet.StudentVisibility))
            return RetakeAccessDenied();

        if (WorksheetInstanceStatusRules.IsRetaking(instance.Status, instance.FirstCompletedAt))
            return RetakeStarted(instanceId, alreadyRetaking: true);
        if (instance.Status != WorksheetInstanceStatus.Completed)
            return ActivityNotCompleted();

        var strategy = _context.Database.CreateExecutionStrategy();
        var reopened = await strategy.ExecuteAsync(async _ =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;

            // Karar bu koşullu UPDATE'te (ön okumalar yalnız hata mesajı içindir). review Ö3: tür (ve silinmemiş etkinlik) aynı
            // WHERE'de — savunma derinliği (tür, oturumu olan sayfada zaten değişemez, #573). FirstCompletedAt migration'dan önce
            // tamamlanmış ve doldurulmamış bir satırda EndTime ile tamamlanır (ilk tamamlanmayı kaybetmemek için).
            var updated = await _context.TestInstances
                .Where(ti => ti.Id == instanceId
                    && ti.Student.UserId == userId
                    && ti.Status == WorksheetInstanceStatus.Completed
                    && _context.Worksheets.IgnoreQueryFilters()
                        .Any(w => w.Id == ti.WorksheetId && w.Kind == WorksheetKind.Activity && !w.IsDeleted))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Started)
                    .SetProperty(ti => ti.FirstCompletedAt, ti => ti.FirstCompletedAt ?? ti.EndTime ?? (DateTime?)now)
                    .SetProperty(ti => ti.AttemptStartedAt, (DateTime?)now)
                    .SetProperty(ti => ti.RetakeCount, ti => ti.RetakeCount + 1)
                    .SetProperty(ti => ti.EndTime, (DateTime?)null)
                    .SetProperty(ti => ti.SolutionsRevealedAt, (DateTime?)null)
                    .SetProperty(ti => ti.CheckCount, 0)
                    .SetProperty(ti => ti.MaxDurationSeconds, (int?)0)
                    .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                    .SetProperty(ti => ti.UpdateUserId, (int?)userId), ct);

            if (updated == 0)
            {
                await tx.RollbackAsync(ct);
                _context.ChangeTracker.Clear();
                return false;
            }

            // Cevaplar ve kontrol sonuçları silinir; satırlar (madde listesi) kalır. AnswerRevision ileri gider (önceki
            // denemenin revizyonları yeniden kullanılmaz); CheckedAnswerRevision null → eski kontrol sonucu yeniden yüklemede dönmez.
            await _context.TestInstanceQuestions
                .Where(q => q.WorksheetInstanceId == instanceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.SelectedAnswerId, (int?)null)
                    .SetProperty(q => q.AnswerPayload, (string?)null)
                    .SetProperty(q => q.IsCorrect, false)
                    .SetProperty(q => q.CorrectCount, 0)
                    .SetProperty(q => q.TotalCount, 0)
                    .SetProperty(q => q.GradedBy, AnswerGradedBy.Auto)
                    .SetProperty(q => q.Feedback, (string?)null)
                    .SetProperty(q => q.CheckedAnswerRevision, (int?)null)
                    .SetProperty(q => q.ShowCorrectAnswer, false)
                    .SetProperty(q => q.TimeTaken, 0)
                    .SetProperty(q => q.AnswerRevision, q => q.AnswerRevision + 1)
                    .SetProperty(q => q.UpdateTime, (DateTime?)now)
                    .SetProperty(q => q.UpdateUserId, (int?)userId), ct);

            await tx.CommitAsync(ct);
            return true;
        }, ct);

        if (reopened)
            return RetakeStarted(instanceId, alreadyRetaking: false);

        // Yarışı başka bir istek kazandı: aynı oturum zaten yeniden açıldıysa idempotent başarı, aksi halde 409.
        var final = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == instanceId && ti.Student.UserId == userId)
            .Select(ti => new { ti.Status, ti.FirstCompletedAt })
            .FirstOrDefaultAsync(ct);
        if (final == null)
            return InstanceNotFound<ActivityRetakeResultDto>();
        return WorksheetInstanceStatusRules.IsRetaking(final.Status, final.FirstCompletedAt)
            ? RetakeStarted(instanceId, alreadyRetaking: true)
            : ActivityNotCompleted();
    }

    private ActivityRetakeResultDto RetakeAccessDenied() => new()
    {
        Success = false,
        Forbidden = true,
        ErrorCode = TestSessionErrorCodes.AccessDenied,
        Message = _localizer["worksheets.session.accessDenied"]
    };

    private ActivityRetakeResultDto RetakeWorksheetNotFound() => new()
    {
        Success = false,
        NotFound = true,
        ErrorCode = TestSessionErrorCodes.WorksheetNotFound,
        Message = _localizer["worksheets.session.activityRetakeWorksheetNotFound"]
    };

    private ActivityRetakeResultDto RetakeStarted(int instanceId, bool alreadyRetaking) => new()
    {
        Success = true,
        InstanceId = instanceId,
        AlreadyRetaking = alreadyRetaking,
        Message = _localizer["worksheets.session.activityRetakeStarted"]
    };

    private ActivityRetakeResultDto ActivityNotCompleted() => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TestSessionErrorCodes.ActivityNotCompleted,
        Message = _localizer["worksheets.session.activityRetakeNotCompleted"]
    };

    /// <summary>
    /// review #571: oturumun worksheet türü — Worksheet soft-delete query filter'ı OLMADAN (sınav sırasında silinen worksheet
    /// de çözülür; oturum sorgusuna JOIN eklenip satır düşürülmez). Bulunamazsa <see cref="WorksheetKind.Test"/>.
    /// </summary>
    private async Task<WorksheetKind> WorksheetKindOfInstanceAsync(int instanceId, CancellationToken ct)
        => await _context.TestInstances.AsNoTracking().IgnoreQueryFilters()
            .Where(ti => ti.Id == instanceId)
            .Select(ti => (WorksheetKind?)ti.Worksheet.Kind)
            .FirstOrDefaultAsync(ct) ?? WorksheetKind.Test;

    /// <summary>
    /// review #571: save-answer ve kontrol ucunun ortak cevap hazırlığı. null = geçersiz (400, hiçbir şey yazılmaz): payload
    /// <see cref="InteractionPayload.MaxLength"/>'ten büyük, tipin kontrolcüsü reddediyor, ya da MCQ'da seçilen şık bu sorunun
    /// (silinmemiş) şıkkı değil. Aksi halde saklanacak (şık, payload): MCQ dışı tipte şık null; payload kontrolcünün
    /// <see cref="IInteractionGrader.Normalize"/>'ıyla (MCQ → null, yerleşimsiz drag-drop → null).
    /// </summary>
    private async Task<(int? SelectedAnswerId, string? Payload)?> PrepareAnswerAsync(
        Question question, IInteractionGrader grader, int? selectedAnswerId, string? rawPayload, CancellationToken ct)
    {
        var payload = string.IsNullOrWhiteSpace(rawPayload) ? null : rawPayload;
        if (payload != null && payload.Length > InteractionPayload.MaxLength)
            return null;
        if (!grader.Validate(question.InteractionPlan, payload))
            return null;
        payload = grader.Normalize(payload);

        var isMcq = grader.InteractionType.Equals(McqGrader.Type, StringComparison.OrdinalIgnoreCase);
        if (!isMcq)
            return (null, payload);

        if (selectedAnswerId != null
            && !await _context.Answers.AsNoTracking().AnyAsync(a => a.Id == selectedAnswerId && a.QuestionId == question.Id, ct))
            return null;

        return (selectedAnswerId, payload);
    }

    private T InvalidCheckRequest<T>() where T : TestSessionResultDto, new() => new()
    {
        Success = false,
        InvalidInput = true,
        ErrorCode = TestSessionErrorCodes.InvalidCheckRequest,
        Message = _localizer["worksheets.session.invalidCheckRequest"]
    };

    private T InstanceNotFound<T>() where T : TestSessionResultDto, new() => new()
    {
        Success = false,
        NotFound = true,
        Message = _localizer["worksheets.session.instanceNotFound"]
    };

    private T NotAnActivity<T>() where T : TestSessionResultDto, new() => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TestSessionErrorCodes.NotAnActivity,
        Message = _localizer["worksheets.session.notAnActivity"]
    };

    /// <summary>Ortak hata sonucunu (kilit / payload reddi) uçun yanıt tipine kopyalar.</summary>
    private static T As<T>(TestSessionResultDto source) where T : TestSessionResultDto, new() => new()
    {
        Success = source.Success,
        Message = source.Message,
        NotFound = source.NotFound,
        Forbidden = source.Forbidden,
        Conflict = source.Conflict,
        InvalidInput = source.InvalidInput,
        ErrorCode = source.ErrorCode,
        Reason = source.Reason
    };

    /// <summary>issue #572: etkinlik tamamlama event'inin transaction öncesi okunan alanları.</summary>
    private sealed record ActivityCompletionPlan(int StudentId, int WorksheetId, int? AssignmentId, int? SubjectId, int? TopicId);

    /// <summary>
    /// issue #572: atama (öğrenciye görünür, oturumu penceresinde sayan — <see cref="AssignmentInstanceWindow.Counts"/>; öğrenci
    /// hedefli olan önce, sonra en yeni), etkinliğin ders/konusu. Worksheet sorgu filtresi yok sayılır (çözüm sırasında silinen
    /// etkinlik de tamamlanır, <see cref="WorksheetKindOfInstanceAsync"/> ile aynı).
    /// </summary>
    private async Task<ActivityCompletionPlan> PlanActivityCompletionAsync(
        int studentId, int worksheetId, DateTime instanceStartTime, CancellationToken ct)
    {
        var worksheet = await _context.Worksheets.AsNoTracking().IgnoreQueryFilters()
            .Where(w => w.Id == worksheetId)
            .Select(w => new { w.SubjectId, w.TopicId })
            .FirstOrDefaultAsync(ct);

        int? assignmentId = null;
        var student = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.GradeId, VerifiedSchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null })
            .FirstOrDefaultAsync(ct);
        if (student != null)
        {
            var planNow = DateTime.UtcNow;
            var windows = await _context.WorksheetAssignments.AsNoTracking()
                .Where(WorksheetStudentAccess.AssignmentVisibleTo(studentId, student.GradeId, student.VerifiedSchoolId))
                .Where(a => a.WorksheetId == worksheetId && a.StartAt <= planNow)
                .OrderByDescending(a => a.StudentId != null).ThenByDescending(a => a.StartAt).ThenByDescending(a => a.Id)
                .Take(ParentAssignmentScope.MaxRows)
                .Select(a => new { a.Id, a.StartAt, a.EndAt })
                .ToListAsync(ct);
            assignmentId = windows
                .Where(w => AssignmentInstanceWindow.Counts(instanceStartTime, WorksheetInstanceStatus.Started, w.StartAt, w.EndAt))
                .Select(w => (int?)w.Id)
                .FirstOrDefault();
        }

        return new ActivityCompletionPlan(studentId, worksheetId, assignmentId, worksheet?.SubjectId, worksheet?.TopicId);
    }

    /// <summary>
    /// issue #572: etkin süre kırpması — <c>clamp(bildirilen, 0, min(<see cref="ActivityOptions.MaxActiveSecondsPerActivity"/>,
    /// windowStart → now sn))</c>. Bildirilen yoksa maddelerin <c>TimeTaken</c> toplamı aynı kuralla. review M1: windowStart
    /// oturumun başlangıcı ya da öğrencinin bu oturumla çakışan başka bir etkinliğinin en geç bitişi (bkz.
    /// <see cref="ActivityWindowStartAsync"/>) — paralel açılan etkinlikler aynı saati birden çok kez saydıramaz.
    /// </summary>
    internal static int ClampActiveSeconds(long reportedOrFallback, DateTime windowStart, DateTime now)
    {
        var elapsed = Math.Max(0L, (long)(now - windowStart).TotalSeconds);
        var upper = Math.Min(ActivityOptions.MaxActiveSecondsPerActivity, elapsed);
        return (int)Math.Clamp(reportedOrFallback, 0L, upper);
    }

    /// <summary>
    /// issue #572 (review M1): süre penceresinin başı = <c>max(StartTime, öğrencinin bu oturum başladıktan SONRA biten diğer
    /// Completed etkinlik oturumlarının en geç EndTime'ı)</c>. Çakışan aralıklar böylece bir kez sayılır: önce biten etkinlik
    /// [başlangıç, bitiş]'i, sonraki yalnız [o bitiş, şimdi]'yi alabilir. <see cref="ActivityCompletionLock"/> altında çağrılır
    /// (eşzamanlı tamamlamalar birbirinin commit'ini görür). Soft-delete edilmiş worksheet'in oturumu da sayılır (süre harcandı).
    /// </summary>
    private async Task<DateTime> ActivityWindowStartAsync(int studentId, int testInstanceId, DateTime startTime, CancellationToken ct)
    {
        // issue #609 (review U1): şu an yeniden çözülen (Started + FirstCompletedAt) diğer etkinliğin EndTime'ı boş; onun daha önce
        // talep ettiği süre en az FirstCompletedAt'e kadar uzanır → bitiş olarak FirstCompletedAt sayılır. Birden çok yeniden
        // çözümde önceki denemelerin son bitişi tutulmaz (sadelik): FirstCompletedAt alt sınırdır, pencere gereğinden geniş
        // kalabilir ama asla negatif/çakışık sayım üretmez; üst sınır yine MaxActiveSecondsPerActivity. Yeniden tamamlandığında
        // EndTime dolar ve normal kurala döner.
        var latestOtherEnd = await _context.TestInstances.AsNoTracking().IgnoreQueryFilters()
            .Where(ti => ti.StudentId == studentId
                && ti.Id != testInstanceId
                && !ti.IsDeleted
                && ((ti.Status == WorksheetInstanceStatus.Completed && ti.EndTime != null && ti.EndTime > startTime)
                    || (ti.Status == WorksheetInstanceStatus.Started && ti.FirstCompletedAt != null && ti.FirstCompletedAt > startTime))
                && _context.Worksheets.IgnoreQueryFilters().Any(w => w.Id == ti.WorksheetId && w.Kind == WorksheetKind.Activity))
            .MaxAsync(ti => ti.Status == WorksheetInstanceStatus.Completed ? ti.EndTime : ti.FirstCompletedAt, ct);
        return latestOtherEnd is { } end && end > startTime ? end : startTime;
    }

    /// <summary>
    /// issue #572: tek <see cref="ActivityCompletedEvent"/> + <c>WorksheetInstance.ActiveSeconds</c>; çağıranın transaction'ında
    /// (koşullu Completed UPDATE'inden SONRA, satır kilidi tutulurken), çağıranın SaveChanges'iyle. Doğru/yanlış sayısı taşınmaz.
    /// </summary>
    private async Task AddActivityCompletedEventAsync(
        ActivityCompletionPlan plan, int testInstanceId, int userId, string? clientId, int? reportedActiveSeconds,
        DateTime startTime, DateTime completedAtUtc, CancellationToken ct)
    {
        // review M1: öğrencinin etkinlik tamamlamaları seri — pencere hesabı önceki (eşzamanlı) tamamlamanın commit'ini görür.
        await _context.Database.AcquireActivityCompletionLockAsync(plan.StudentId, ct);

        // Oturumun kendi (silinmemiş) satırları; sorgu filtreleri bilerek yok sayılır (review minor): madde sayısı, süre ve ders
        // yedeği aynı kümeden gelir — çözüm sırasında soru / worksheet-soru bağı soft-delete edilse de madde çözülmüştür ve
        // dersi anlamlıdır (WorksheetKindOfInstanceAsync ile aynı yaklaşım).
        var rows = _context.TestInstanceQuestions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => q.WorksheetInstanceId == testInstanceId && !q.IsDeleted);
        var itemCount = await rows.CountAsync(ct);
        // M2: cevaplı madde sayısı — Completed + satır kilidi altında, sonradan cevap yazılamaz → kesin.
        var answeredItemCount = await rows.CountAsync(q => q.SelectedAnswerId != null || q.AnswerPayload != null, ct);

        long reported = reportedActiveSeconds
            ?? await rows.SumAsync(q => (long)q.TimeTaken, ct);
        // issue #609 (review L3): yeniden çözülen etkinlikte bu denemenin başlangıcı AttemptStartedAt (StartTime ilk denemenin).
        var attemptStartedAt = await _context.TestInstances.AsNoTracking().IgnoreQueryFilters()
            .Where(ti => ti.Id == testInstanceId)
            .Select(ti => ti.AttemptStartedAt)
            .FirstOrDefaultAsync(ct);
        var attemptStart = attemptStartedAt is { } a && a > startTime ? a : startTime;
        var windowStart = await ActivityWindowStartAsync(plan.StudentId, testInstanceId, attemptStart, ct);
        var activeSeconds = ClampActiveSeconds(reported, windowStart, completedAtUtc);

        // Etkinliğin dersi yoksa maddelerin en sık dersi (eşitlikte küçük id — deterministik).
        var subjectId = plan.SubjectId ?? await rows
            .Where(q => q.WorksheetQuestion.Question.SubjectId != null)
            .GroupBy(q => q.WorksheetQuestion.Question.SubjectId)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefaultAsync(ct);

        // issue #609: yeniden çözülen etkinlikte etkin süre birikir (önceki denemelerin süresi + bu deneme); ilk tamamlanmada
        // ActiveSeconds null olduğundan değer aynen bu denemeninkidir. Event yalnız bu denemenin süresini taşır.
        await _context.TestInstances
            .Where(ti => ti.Id == testInstanceId)
            .ExecuteUpdateAsync(s => s.SetProperty(ti => ti.ActiveSeconds, ti => (int?)((ti.ActiveSeconds ?? 0) + activeSeconds)), ct);

        var outboxId = Guid.NewGuid();
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            Type = OutboxEventRegistry.NameFor<ActivityCompletedEvent>(),
            Content = JsonSerializer.Serialize(new ActivityCompletedEvent
            {
                EventId = outboxId,
                UserId = userId,
                StudentId = plan.StudentId,
                ClientId = string.IsNullOrEmpty(clientId) ? null : clientId,
                ActivityId = plan.WorksheetId,
                WorksheetInstanceId = testInstanceId,
                AssignmentId = plan.AssignmentId,
                SubjectId = subjectId,
                TopicId = plan.TopicId,
                ItemCount = itemCount,
                AnsweredItemCount = answeredItemCount,
                ActiveSeconds = activeSeconds,
                CompletedAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc)
            }),
            CreatedAt = completedAtUtc
        });
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

        // Toplam + doğru (öğrencinin sonuç ekranıyla aynı formül; issue #571: WorksheetScoring hibrit kuralı — etkileşimli
        // cevaplar da sayılır). UPDATE satır kilidi tutulduğu için iki sayım tutarlı.
        var rows = _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstanceId == testInstanceId);
        var total = await rows.CountAsync(ct);
        var correct = await rows.CountAsync(WorksheetScoring.IsCorrectRow, ct);
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
